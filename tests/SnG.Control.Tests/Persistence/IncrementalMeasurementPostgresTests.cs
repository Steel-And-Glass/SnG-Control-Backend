using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;

public sealed class IncrementalMeasurementPostgresTests
{
    [VerificationPostgresFact]
    public async Task ThreeThenFourRetainsEvidenceChecksAndOpenWorkspace()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        SnGControlDbContext Db() => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).Options);
        using var files = new TemporaryStorage(); var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = files.Root }));
        var clock = new ControlledTimeProvider(TestTiming.Value.UtcNow); var timing = new SnG.Control.Application.Scheduling.TaskTiming(clock, new());
        EfMeasurementStore Store(SnGControlDbContext c) => new(c, timing, new(c, new ProjectNotificationPlanner(timing)), storage, new IdentityDouble("engineer"), new(DateTime.UnixEpoch));
        EfEngineeringStore Engineer(SnGControlDbContext c) => new(c, timing, new(c, new ProjectNotificationPlanner(timing)), storage, new IdentityDouble("engineer"));
        var actor = new DecisionActor("installer", "installer@example.test", "Installer"); var now = clock.Now;
        await using var db = Db(); await db.Database.MigrateAsync();
        var client = new Client("Measurement synthetic"); var project = new Project(client.Id, "Measurement synthetic", "City", ProductLine.Classic, "advisor");
        db.AddRange(client, project); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
        var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1)); present.Start(now.AddDays(-1), now.AddDays(1)); present.Complete(now);
        var decision = new ClientQuotationDecision(project.Id, quote.Id, present.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null);
        var accounting = new ProjectTask(project.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now); accounting.StartUntracked(now); accounting.Complete(now);
        var handoff = new AccountingHandoff(project.Id, decision.Id, quote.Id, 1, accounting.Id, now, "advisor", null, null);
        var task = new ProjectTask(project.Id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, "installer", now);
        db.AddRange(version, catalog, quote, present, decision, accounting, handoff, new InitialDeposit(handoff, 50, now, now, null, null, "accountant", null, null), task);
        foreach (var (quantity, index) in new[] { 10 }.Select((q, i) => (q, i)))
        {
            var values = new CatalogItemValues((index + 1).ToString(), "Room", "Window", null, null, null, null, 1.2m, 2, 2.4m, quantity);
            var source = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, index + 1, values);
            db.Add(new CommercialCatalogItemExtraction(source.Id, values, CatalogItemImageStatus.Stored, $"projects/{project.Id:D}/budget-quotations/{version.Id:D}/images/{source.Id:D}.png", "image/png"));
            db.AddRange(source, new CommercialQuotationItem(quote.Id, source.Id, CommercialItemSection.Main, index + 1, values, 10, 100));
        }

        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Store(db).InitializeAsync(project.Id, actor, default);
        db.ChangeTracker.Clear();
        var checklist = await db.MeasurementChecklists.SingleAsync(c => c.ProjectId == project.Id);
        Assert.True(checklist.IsIncremental);
        var all = await db.MeasurementStructures.Where(s => s.ProjectId == project.Id).OrderBy(s => s.Id).ToArrayAsync();
        foreach (var row in all.Take(7))
        {
            row.RecordActual(1.2m, 2, "Window", now, actor.Subject); row.RecordSiteDetails("Agreed", "Room", null, now, actor.Subject);
            row.RecordTechnical(new("NotApplicable", null, "Outside", "NotApplicable", null, null, false, null), now, actor.Subject);
            row.ResolveComparison("Comparable", "Synthetic comparison", now, actor.Subject); row.Measure(true, now, actor.Subject);
        }
        var doc = new ProjectDocument(Guid.NewGuid(), project.Id, "evidence.pdf", "application/pdf", 3, Guid.NewGuid(), now, actor.Subject, null, null, isMeasurementDocument: true);
        db.Add(doc); await storage.SaveAsync(doc.StorageKey, new byte[]{1,2,3}, default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var firstIds = all.Take(3).Select(s => s.Id).ToArray(); var nextIds = all.Skip(3).Take(4).Select(s => s.Id).ToArray();
        var first = new MeasurementSubmissionCommand(Guid.NewGuid(), 0, firstIds, [new(doc.Id, firstIds)]);
        await Error("MeasurementAccessDenied", () => Store(db).SubmitAsync(project.Id, first, actor with { Subject="other" }, default));
        await Error("MeasurementsIncomplete", () => Store(db).SubmitAsync(project.Id, first with { StructureIds=[all[9].Id], Documents=[] }, actor, default));
        await Error("MeasurementStructureNotFound", () => Store(db).SubmitAsync(project.Id, first with { StructureIds=[Guid.NewGuid()], Documents=[] }, actor, default));
        await Error("MeasurementDocumentsChanged", () => Store(db).SubmitAsync(project.Id, first with { Documents=[new(Guid.NewGuid(), firstIds)] }, actor, default));
        await using var a = Db(); await using var b = Db();
        var replies = await Task.WhenAll(Store(a).SubmitAsync(project.Id, first, actor, default), Store(b).SubmitAsync(project.Id, first, actor, default));
        Assert.Equal(replies[0].Id, replies[1].Id); Assert.Equal(1, replies[0].Number);
        var report = (await Store(db).ReadReportAsync(project.Id, replies[0].Id, new("installer", SystemRole.Installation), default))!;
        Assert.EndsWith("-Tramo 01.pdf", report.FileName); Assert.True(report.Bytes.Length > 1000);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(report.Bytes), @"/Type /Page\b"));
        Assert.Null(await Store(db).ReadReportAsync(project.Id, replies[0].Id, new("other", SystemRole.Installation), default));
        var state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        Assert.True(state.CanModify); Assert.Equal("InProgress", state.TaskStatus); Assert.Null(state.SubmissionId); Assert.Equal(10, state.Structures.Count);
        Assert.Equal(3, Assert.Single(state.Deliveries!).StructureIds.Count);
        var engineeringState = (await Engineer(db).ReadAsync(project.Id, new("engineer", SystemRole.Engineering), default))!;
        Assert.True(engineeringState.IsIncremental); Assert.Equal(3, engineeringState.Total); Assert.Null(engineeringState.ChecklistId);
        var engineer = new DecisionActor("engineer", null, "Engineer"); var check = new SnG.Control.Application.Features.Engineering.EngineeringCheck(Guid.NewGuid(), 0, true);
        await Engineer(db).CheckAsync(project.Id, firstIds[0], check, engineer, default);
        await Engineer(db).CheckAsync(project.Id, firstIds[0], check, engineer, default);
        await Error("EngineeringRequestConflict", () => Engineer(db).CheckAsync(project.Id, firstIds[0], check with {Completed=false}, engineer, default));
        await Error("EngineeringRevisionConflict", () => Engineer(db).CheckAsync(project.Id, firstIds[0], check with {RequestKey=Guid.NewGuid()}, engineer, default));
        await Error("EngineeringAccessDenied", () => Engineer(db).CheckAsync(project.Id, firstIds[1], check, engineer with {Subject="other"}, default));
        await Error("EngineeringLocked", () => Engineer(db).CloseAsync(project.Id, new(Guid.NewGuid(), 0, doc.Id), engineer, default));
        await Error("EngineeringLocked", () => Engineer(db).InitializeHistoricalAsync(project.Id, new(replies[0].Id, "No legacy bypass"), engineer, default));
        await Error("EngineeringLocked", () => Engineer(db).UploadAsync(project.Id, Guid.NewGuid(), 0, NoUploads(), engineer, default));
        await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "Submit", DocumentIds:[doc.Id]), actor, default));
        foreach (var action in new[]{"Measure","Retire","RecordActual","RecordTechnical","RecordSketch","Reinstate"})
            await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, action, firstIds[0], false), actor, default));
        await Error("MeasurementRequestConflict", () => Store(db).SubmitAsync(project.Id, first with { Documents=[] }, actor, default));
        await Error("MeasurementsLocked", () => Store(db).SubmitAsync(project.Id, first with { RequestKey=Guid.NewGuid(), ExpectedRevision=state.Revision }, actor, default));
        await Error("MeasurementRevisionConflict", () => Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), 0, nextIds, []), actor, default));
        var second = await Store(db).SubmitAsync(project.Id, new(Guid.NewGuid(), state.Revision, nextIds, [new(doc.Id,nextIds)]), actor, default);
        Assert.Equal(2, second.Number);
        var secondPdf = (await Store(db).ReadReportAsync(project.Id, second.Id, new("engineer", SystemRole.Engineering), default))!;
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(secondPdf.Bytes), @"/Type /Page\b").Count);
        Assert.EndsWith("-Tramo 02.pdf", secondPdf.FileName);
        engineeringState = (await Engineer(db).ReadAsync(project.Id, new("engineer", SystemRole.Engineering), default))!;
        Assert.Equal(7, engineeringState.Total); Assert.Equal(1, engineeringState.Completed);
        Assert.Equal(Enumerable.Repeat(1,3).Concat(Enumerable.Repeat(2,4)), engineeringState.Structures.Select(s => s.Origin!.Number));
        Assert.Equal(1, engineeringState.Structures.Single(s => s.Structure.Id == firstIds[0]).Revision);
        Assert.Equal(report.Bytes, (await Store(db).ReadReportAsync(project.Id, replies[0].Id, new("installer", SystemRole.Installation), default))!.Bytes);
        Assert.Equal(replies[0].Documents.Single().Sha256, second.Documents.Single().Sha256);
        Assert.Equal(firstIds.Order(), replies[0].Documents.Single().StructureIds.Order());
        Assert.Equal(nextIds.Order(), second.Documents.Single().StructureIds.Order());
        state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        await Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "Add", Description:"New onsite",Width:1,Height:2), actor, default);
        state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        Assert.Equal(11, state.Structures.Count); Assert.Equal(2, state.Deliveries!.Count);
        db.ChangeTracker.Clear();
        await Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "Retire", all[9].Id), actor, default);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MeasurementStructures\" SET \"ActualWidth\"=2 WHERE \"Id\"={firstIds[0]}"));
            Assert.Equal("23514", violation.SqlState); await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        var openChecklist = await db.MeasurementChecklists.SingleAsync(c => c.ProjectId == project.Id);
        var remaining = await db.MeasurementStructures.Where(r => r.Id == all[7].Id || r.Id == all[8].Id).ToArrayAsync();
        foreach (var row in remaining)
        {
            row.RecordActual(1.2m, 2, "Window", now, actor.Subject); row.RecordSiteDetails("Agreed", "Room", null, now, actor.Subject);
            row.RecordTechnical(new("NotApplicable", null, "Outside", "NotApplicable", null, null, false, null), now, actor.Subject);
            row.ResolveComparison("Comparable", "Synthetic comparison", now, actor.Subject); row.Measure(true, now, actor.Subject);
        }
        openChecklist.Advance(); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        var commands = remaining.Select(r => new MeasurementSubmissionCommand(Guid.NewGuid(), state.Revision, [r.Id], [])).ToArray();
        async Task<(int Status, MeasurementSubmissionCommand Command)> Send(MeasurementSubmissionCommand c)
        {
            await using var connection = Db();
            try { await Store(connection).SubmitAsync(project.Id, c, actor, default); return (201,c); }
            catch (DocumentException conflict) { Assert.Equal("MeasurementRevisionConflict",conflict.Code); return (conflict.Status,c); }
        }
        var disjoint = await Task.WhenAll(commands.Select(Send));
        Assert.Single(disjoint, result => result.Status == 201); var loser = Assert.Single(disjoint, result => result.Status == 409);
        state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        await Store(db).SubmitAsync(project.Id, loser.Command with { RequestKey=Guid.NewGuid(), ExpectedRevision=state.Revision }, actor, default);
        engineeringState = (await Engineer(db).ReadAsync(project.Id, new("engineer", SystemRole.Engineering), default))!;
        Assert.Equal(9,engineeringState.Total);Assert.Equal(1,engineeringState.Completed);
        Assert.Equal(4,engineeringState.Deliveries!.Count);
        Assert.Single(await db.ProjectTasks.Where(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.PrepareOpeningRecord).ToArrayAsync());
        Assert.Empty(await db.EngineeringChecklists.Where(c => c.ProjectId == project.Id).ToArrayAsync());
        Assert.Single(await db.Set<SnG.Control.Domain.Entities.Incremental.EngineeringCheckEvent>().Where(e => e.ProjectId == project.Id).ToArrayAsync());
    }
    private static async IAsyncEnumerable<DocumentUpload> NoUploads() { await Task.CompletedTask; yield break; }
    private static async Task Error(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<DocumentException>(action)).Code);
}
