using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;

public sealed class VerificationPostgresFactAttribute : FactAttribute
{
    public VerificationPostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"))) Skip = "Requires isolated synthetic verification_test PostgreSQL."; }
}
public sealed class DepositVerificationPostgresTests
{
    [VerificationPostgresFact]
    public async Task ReceiptsRejectionResubmissionAtomicApprovalConcurrencyAndRollback()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        SnGControlDbContext Db(bool fail = false) => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).AddInterceptors(new FailSave(fail)).Options);
        EfDepositVerificationStore Store(SnGControlDbContext db) => new(db, TestTiming.Value, new(db, new ProjectNotificationPlanner(TestTiming.Value)));
        using var files = new TemporaryStorage(); var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = files.Root }));
        ProjectDocumentAccess Access(string subject = "advisor", SystemRole role = SystemRole.CommercialAdvisor) => new(new IdentityDouble(subject), new ActiveRoleDouble(role), new IdentityDouble("budgets"));
        EfProjectDocumentStore Documents(SnGControlDbContext db, string subject = "advisor", SystemRole role = SystemRole.CommercialAdvisor) => new(db, storage, Access(subject, role), TestTiming.Value);
        var actor = new DecisionActor("advisor", "advisor@example.test", "Advisor"); var accountant = new DecisionActor("accountant", null, "Accountant"); var now = TestTiming.Value.UtcNow;
        await using var db = Db(); await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        var client = new Client("Verification synthetic client"); var project = new Project(client.Id, "Verification synthetic", "City", ProductLine.Classic, "advisor");
        var other = new Project(client.Id, "Other synthetic", "City", ProductLine.Classic, "other");
        db.AddRange(client, project, other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Error("ApprovedQuotationRequired", () => Documents(db).UploadReceiptsAsync(project.Id, Uploads(("early.pdf", null)), default));
        await Error("ApprovedQuotationRequired", () => Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), [Guid.NewGuid()]), actor, "accountant", default));
        Assert.Empty(Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories));
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
        var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1));
        present.Start(now.AddDays(-1), now.AddDays(1)); present.Complete(now);
        db.AddRange(version, catalog, quote, present, new ClientQuotationDecision(project.Id, quote.Id, present.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Error("DocumentAccessDenied", () => Documents(db, "other").UploadReceiptsAsync(project.Id, Uploads(("foreign.pdf", null)), default));
        await Error("DocumentAccessDenied", () => Documents(db, "advisor", SystemRole.Management).UploadReceiptsAsync(project.Id, Uploads(("management.pdf", null)), default));
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Documents(failed).UploadReceiptsAsync(project.Id, Uploads(("rollback.pdf", null)), default));
        Assert.Empty(Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories)); Assert.False(await db.ProjectDocuments.Where(x => x.ProjectId == project.Id).AnyAsync());
        var upload = await Documents(db).UploadReceiptsAsync(project.Id, Uploads(("receipt-a.pdf", null), ("receipt-b.pdf", null)), default); db.ChangeTracker.Clear();
        var ids = upload.Documents.Select(d => d.Id).ToArray(); Assert.Equal(2, ids.Length);
        Assert.False(await db.AccountingHandoffs.Where(x => x.ProjectId == project.Id).AnyAsync()); Assert.False(await db.DepositVerifications.Where(x => x.ProjectId == project.Id).AnyAsync());
        Assert.False(await db.ProjectTasks.Where(x => x.ProjectId == project.Id).AnyAsync(t => t.Type == ProjectTaskType.TakeMeasurements));
        var originalBytes = await storage.ReadAsync(ProjectDocument.Key(project.Id, ids[0]), default);
        var foreign = new ProjectDocument(Guid.NewGuid(), other.Id, "foreign.pdf", null, 3, Guid.NewGuid(), now, "other", null, null, true);
        var general = new ProjectDocument(Guid.NewGuid(), project.Id, "plan.pdf", null, 3, Guid.NewGuid(), now, "advisor", null, null);
        db.AddRange(foreign, general); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        foreach (var bad in new[] { foreign.Id, general.Id, Guid.NewGuid() }) await Error("InvalidVerificationDocuments", () => Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), [bad]), actor, "accountant", default));
        await Error("VerificationAccessDenied", () => Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), ids), actor with { Subject = "other" }, "accountant", default));
        var submission = new VerificationSubmission(Guid.NewGuid(), ids);
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).SubmitAsync(project.Id, submission, actor, "accountant", default));
        Assert.False(await db.AccountingHandoffs.Where(x => x.ProjectId == project.Id).AnyAsync()); Assert.False(await db.DepositVerifications.Where(x => x.ProjectId == project.Id).AnyAsync());
        await using var a = Db(); await using var b = Db();
        var sent = await Task.WhenAll(Store(a).SubmitAsync(project.Id, submission, actor, "accountant", default), Store(b).SubmitAsync(project.Id, submission, actor, "accountant", default));
        Assert.Equal(sent[0], sent[1]); var reviewId = sent[0];
        Assert.Equal(1, await db.AccountingHandoffs.Where(x => x.ProjectId == project.Id).CountAsync()); Assert.Equal(1, await db.DepositVerifications.Where(x => x.ProjectId == project.Id).CountAsync());
        Assert.Equal(2, await db.DepositVerificationDocuments.Where(x => x.ProjectId == project.Id).CountAsync());
        Assert.Equal(1, await db.ProjectNotifications.Where(x => x.ProjectId == project.Id).CountAsync(n => n.Kind == NotificationKind.DepositVerificationRequested));
        await Error("VerificationRequestConflict", () => Store(db).SubmitAsync(project.Id, submission with { DocumentIds = [ids[0]] }, actor, "accountant", default));
        await Error("VerificationDocumentsLocked", () => Store(db).SubmitAsync(project.Id, submission with { RequestKey = Guid.NewGuid() }, actor, "accountant", default));
        await Error("VerificationDocumentsLocked", () => Documents(db).UploadReceiptsAsync(project.Id, Uploads(("blocked.pdf", null)), default));
        await Error("InvalidReplacement", () => Documents(db).UploadAsync(project.Id, Uploads(("bypass.pdf", ids[0])), default));
        Assert.Null(await Store(db).ReadAsync(project.Id, new("other", SystemRole.Accounting), default));
        var management = (await Store(db).ReadAsync(project.Id, new("manager", SystemRole.Management), default))!;
        Assert.False(management.CanManage); Assert.False(management.CanSubmit); Assert.False(management.CanReview); Assert.Equal(ids.Order(), Assert.Single(management.Reviews).DocumentIds);
        Assert.True((await Store(db).ReadAsync(project.Id, new("accountant", SystemRole.Accounting), default))!.CanReview);
        await Error("VerificationNotFound", () => Store(db).DecideAsync(other.Id, reviewId, false, null, "No", accountant, "installer", default));
        await Error("VerificationAccessDenied", () => Store(db).DecideAsync(project.Id, reviewId, false, null, "No", actor, "installer", default)); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ArgumentException>(() => Store(db).DecideAsync(project.Id, reviewId, false, null, " ", accountant, "installer", default)); db.ChangeTracker.Clear();
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).DecideAsync(project.Id, reviewId, false, null, "No", accountant, "installer", default));
        Assert.False(await db.ProjectTasks.AnyAsync(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.CorrectDepositReceipts));
        await using var rejectA = Db(); await using var rejectB = Db();
        async Task<string> Reject(SnGControlDbContext context) { try { await Store(context).DecideAsync(project.Id, reviewId, false, null, "  Comprobante ilegible  ", accountant, "installer", default); return "OK"; } catch (DocumentException e) { return e.Code; } }
        var rejected = await Task.WhenAll(Reject(rejectA), Reject(rejectB));
        Assert.Single(rejected, x => x == "OK"); Assert.Single(rejected, x => x == "VerificationAlreadyDecided");
        db.ChangeTracker.Clear();
        var correction = await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.CorrectDepositReceipts);
        Assert.Equal("advisor", correction.AssignedToGoogleSubject); Assert.Equal(ProjectTaskStatus.InProgress, correction.Status);
        Assert.Null(correction.QuotationRound); Assert.Null(correction.DeadlineAt); Assert.Null(correction.WasOverdue);
        Assert.Equal(correction.Id, (await db.DepositVerifications.AsNoTracking().SingleAsync(r => r.Id == reviewId)).CorrectionTaskId);
        var inbox = new EfProjectTaskStore(db, TestTiming.Value, new(db, new ProjectNotificationPlanner(TestTiming.Value)));
        Assert.Contains(await inbox.ListByAreaAsync(OperationalArea.Commercial, ProjectTaskStatus.InProgress, new("advisor", SystemRole.CommercialAdvisor), default), t => t.Id == correction.Id);
        Assert.DoesNotContain(await inbox.ListByAreaAsync(OperationalArea.Commercial, ProjectTaskStatus.InProgress, new("other", SystemRole.CommercialAdvisor), default), t => t.Id == correction.Id);
        Assert.DoesNotContain(await inbox.ListByAreaAsync(OperationalArea.Commercial, ProjectTaskStatus.InProgress, new("advisor", SystemRole.CommercialHead), default), t => t.Id == correction.Id);
        Assert.Contains(await new EfManagementReadStore(db).ListTasksAsync(default), t => t.Task.Id == correction.Id);
        Assert.True(await db.ProjectNotifications.AnyAsync(n => n.ProjectTaskId == correction.Id && n.Kind == NotificationKind.TaskAssigned && n.RecipientGoogleSubject == "advisor"));

        Assert.Equal("Comprobante ilegible", (await db.DepositVerifications.Where(x => x.ProjectId == project.Id).SingleAsync(r => r.Id == reviewId)).Note); db.ChangeTracker.Clear();
        Assert.False(await db.InitialDeposits.Where(x => x.ProjectId == project.Id).AnyAsync()); Assert.False(await db.ProjectTasks.Where(x => x.ProjectId == project.Id).AnyAsync(t => t.Type == ProjectTaskType.TakeMeasurements));
        Assert.Equal(ProjectTaskStatus.Pending, (await db.ProjectTasks.Where(x => x.ProjectId == project.Id).SingleAsync(t => t.Type == ProjectTaskType.RegisterInitialDeposit)).Status); db.ChangeTracker.Clear();
        Assert.True(await db.ProjectNotifications.Where(x => x.ProjectId == project.Id).AnyAsync(n => n.Kind == NotificationKind.DepositVerificationRejected && n.RecipientGoogleSubject == "advisor"));
        var replacement = await Documents(db).UploadReceiptsAsync(project.Id, Uploads(("corrected.pdf", ids[0])), default); db.ChangeTracker.Clear();
        Assert.Equal(originalBytes, await storage.ReadAsync(ProjectDocument.Key(project.Id, ids[0]), default));
        var corrected = replacement.Documents.Single().Id;
        var resubmission = new VerificationSubmission(Guid.NewGuid(), [corrected, ids[1]]);
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).SubmitAsync(project.Id, resubmission, actor, "accountant", default));
        Assert.Equal(ProjectTaskStatus.InProgress, (await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == correction.Id)).Status);
        var next = await Store(db).SubmitAsync(project.Id, resubmission, actor, "accountant", default); db.ChangeTracker.Clear();
        Assert.Equal(next, await Store(db).SubmitAsync(project.Id, resubmission, actor, "accountant", default));
        Assert.Equal(ProjectTaskStatus.Completed, (await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == correction.Id)).Status);

        Assert.NotEqual(reviewId, next); Assert.Equal(2, await db.DepositVerifications.Where(x => x.ProjectId == project.Id).CountAsync()); Assert.Equal(1, await db.AccountingHandoffs.Where(x => x.ProjectId == project.Id).CountAsync());
        await Store(db).DecideAsync(project.Id, next, false, null, "Second rejection", accountant, "installer", default); db.ChangeTracker.Clear();
        var secondCorrection = await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.CorrectDepositReceipts && t.Status == ProjectTaskStatus.InProgress);
        Assert.NotEqual(correction.Id, secondCorrection.Id);
        next = await Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), [corrected, ids[1]]), actor, "accountant", default); db.ChangeTracker.Clear();
        Assert.Equal(2, await db.ProjectTasks.CountAsync(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.CorrectDepositReceipts && t.Status == ProjectTaskStatus.Completed));
        var input = new DepositInput(5000000.005m, now.AddHours(-2), null, "Verified");
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).DecideAsync(project.Id, next, true, input, "Verified", accountant, "installer", default));
        Assert.False(await db.InitialDeposits.Where(x => x.ProjectId == project.Id).AnyAsync()); Assert.False(await db.ProjectTasks.Where(x => x.ProjectId == project.Id).AnyAsync(t => t.Type == ProjectTaskType.TakeMeasurements));
        Assert.Equal(DepositVerificationStatus.Pending, (await db.DepositVerifications.Where(x => x.ProjectId == project.Id).AsNoTracking().SingleAsync(r => r.Id == next)).Status);
        await using var c = Db(); await using var d = Db();
        async Task<string> Approve(SnGControlDbContext context) { try { await Store(context).DecideAsync(project.Id, next, true, input, "Verified", accountant, "installer", default); return "OK"; } catch (DocumentException e) { return e.Code; } }
        var decisions = await Task.WhenAll(Approve(c), Approve(d)); Assert.Single(decisions, x => x == "OK"); Assert.Single(decisions, x => x == "VerificationAlreadyDecided");
        var deposit = await db.InitialDeposits.Where(x => x.ProjectId == project.Id).AsNoTracking().SingleAsync(); Assert.Equal(5000000.01m, deposit.Amount); Assert.Equal(quote.Id, deposit.CommercialQuotationId);
        var task = await db.ProjectTasks.Where(x => x.ProjectId == project.Id).AsNoTracking().SingleAsync(t => t.Type == ProjectTaskType.RegisterInitialDeposit);
        Assert.Equal(ProjectTaskStatus.Completed, task.Status); Assert.Equal(deposit.RecordedAt, task.CompletedAt); Assert.Equal(deposit.RecordedAt, task.StartedAt);
        var installation = await db.ProjectTasks.Where(x => x.ProjectId == project.Id).AsNoTracking().SingleAsync(t => t.Type == ProjectTaskType.TakeMeasurements);
        Assert.Equal(OperationalArea.Installation, installation.Area); Assert.Equal("installer", installation.AssignedToGoogleSubject); Assert.Null(installation.QuotationRound); Assert.Null(installation.DeadlineAt); Assert.Null(installation.WasOverdue);
        Assert.Equal(ProjectTaskStatus.Pending, installation.Status);
        Assert.True(await db.ProjectNotifications.Where(x => x.ProjectId == project.Id).AnyAsync(n => n.Kind == NotificationKind.DepositVerificationApproved && n.RecipientGoogleSubject == "advisor"));
        Assert.True(await db.ProjectNotifications.Where(x => x.ProjectId == project.Id).AnyAsync(n => n.Kind == NotificationKind.TaskAssigned && n.ProjectTaskId == installation.Id && n.RecipientGoogleSubject == "installer"));
        await Error("VerificationDocumentsLocked", () => Documents(db).UploadReceiptsAsync(project.Id, Uploads(("late.pdf", null)), default));
        var final = (await Store(db).ReadAsync(project.Id, new("manager", SystemRole.Management), default))!;
        Assert.True(final.Accounting.Deposit.Available); Assert.False(final.CanManage); Assert.False(final.CanReview);
        Assert.Equal(3, final.Reviews.Count); Assert.Contains(ids[0], final.Reviews.Single(r => r.Id == reviewId).DocumentIds); Assert.Contains(final.Documents, x => x.Id == ids[0] && !x.IsCurrent);
        Assert.Equal(originalBytes, await storage.ReadAsync(ProjectDocument.Key(project.Id, ids[0]), default));
    }
    private static async Task Error(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<DocumentException>(action)).Code);
    private static async IAsyncEnumerable<DocumentUpload> Uploads(params (string Name, Guid? Replaces)[] files)
    {
        for (var i = 0; i < files.Length; i++) { using var stream = new MemoryStream([1, 2, 3]); yield return new($"file-{i}", files[i].Name, "application/pdf", files[i].Replaces, stream); }
        await Task.CompletedTask;
    }
    private sealed class FailSave(bool fail) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (fail) throw new InvalidOperationException("Synthetic pre-commit failure"); return ValueTask.FromResult(result); }
    }
}
