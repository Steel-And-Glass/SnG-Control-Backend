using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;
internal static class EngineeringPostgresChecks
{
    public static async Task VerifyAsync(Func<SnGControlDbContext> open, Guid id, IStreamingFileStorage storage, TaskTiming timing, MeasurementState source)
    {
        EfEngineeringStore Store(SnGControlDbContext db) => new(db, timing, new(db, new ProjectNotificationPlanner(timing)), storage, new IdentityDouble("engineer"), new OpeningRoles());
        var actor = new DecisionActor("engineer", "engineer@example.test", "Engineer");
        async Task<EngineeringState> Read() { await using var db = open(); return (await Store(db).ReadAsync(id, new("engineer", SystemRole.Engineering), default))!; }
        async Task Check(Guid sid, EngineeringCheck command) { await using var db = open(); await Store(db).CheckAsync(id, sid, command, actor, default); }
        async Task Error(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<DocumentException>(action)).Code);
        var state = await Read();
        Assert.Equal(source.SubmissionId, state.SubmissionId); Assert.Equal("InProgress", state.TaskStatus);
        Assert.Equal(source.Structures.Where(s => s.RetiredAt == null).Select(s => s.Id), state.Structures.Select(s => s.Structure.Id));
        Assert.Equal(source.Structures.Where(s => s.RetiredAt == null), state.Structures.Select(s => s.Structure));
        Assert.Equal(0, state.Completed); Assert.Equal(state.Total, state.Pending);
        await using var verify = open();
        Assert.Single(await verify.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id && t.Type == ProjectTaskType.PrepareOpeningRecord).ToArrayAsync());
        Assert.Null((await verify.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == state.TaskId)).DeadlineAt);
        Assert.Single(await verify.ProjectNotifications.Where(n => n.ProjectTaskId == state.TaskId && n.Kind == NotificationKind.TaskAssigned).ToArrayAsync());
        Assert.Null(await Store(verify).ReadAsync(id, new("other", SystemRole.Engineering), default));
        Assert.False((await Store(verify).ReadAsync(id, new("manager", SystemRole.Management), default))!.CanModify);
        // Synthetic legacy fixture: remove only this test project's newly created Engineering work.
        // Installation's immutable submission/report remain untouched.
        // Test-fixture construction only: emulate a pre-Engineering installation with no additive
        // Engineering projection. Session-local trigger bypass never exists in application code.
        await using (var fixture = await verify.Database.BeginTransactionAsync())
        {
            await verify.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            await verify.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "ActiveOpeningRecordMembers" WHERE "ProjectId"={id};
                DELETE FROM "OpeningRecordMembers" WHERE "ProjectId"={id};
                DELETE FROM "OpeningRecords" WHERE "ProjectId"={id};
                DELETE FROM "EngineeringEvidenceChecks" WHERE "ProjectId"={id};
                DELETE FROM "EngineeringReceipts" WHERE "ProjectId"={id};
                DELETE FROM "EngineeringWorkspaces" WHERE "ProjectId"={id};
                DELETE FROM "WorkPackageMembers" WHERE "AssignmentId"={state.TaskId};
                DELETE FROM "WorkPackageAssignments" WHERE "LegacyTaskId"={state.TaskId};
                """);
            await fixture.CommitAsync();
        }
        await verify.ProjectNotifications.Where(n => n.ProjectTaskId == state.TaskId).ExecuteDeleteAsync();
        await verify.EngineeringEvents.Where(e => e.ProjectId == id).ExecuteDeleteAsync();
        await verify.EngineeringStructures.Where(e => e.ProjectId == id).ExecuteDeleteAsync();
        await verify.EngineeringChecklists.Where(e => e.ProjectId == id).ExecuteDeleteAsync();
        await verify.ProjectTasks.Where(t => t.Id == state.TaskId).ExecuteDeleteAsync();
        await Error("EngineeringSourceInvalid", () => Store(verify).InitializeHistoricalAsync(id, new(Guid.NewGuid(), "Wrong source"), actor, default));
        await Store(verify).InitializeHistoricalAsync(id, new(source.SubmissionId!.Value, "Explicit synthetic legacy initialization"), actor, default);
        state = await Read();
        Assert.Equal(source.Structures.Where(s => s.RetiredAt == null).Select(s => s.Id), state.Structures.Select(s => s.Structure.Id));
        Assert.Equal("Explicit synthetic legacy initialization", Assert.Single(state.History).Reason);
        var first = state.Structures[0]; var second = state.Structures[1];
        var mark = new EngineeringCheck(Guid.NewGuid(), 0, true);
        await Task.WhenAll(Check(first.Structure.Id, mark), Check(first.Structure.Id, mark));
        state = await Read(); Assert.Equal(1, state.Completed); Assert.Single(state.History, h => h.Action == "Check");
        await Error("EngineeringRequestConflict", () => Check(first.Structure.Id, mark with { Completed = false }));
        await Error("EngineeringRevisionConflict", () => Check(first.Structure.Id, mark with { RequestKey = Guid.NewGuid() }));
        await Error("EngineeringAccessDenied", () => Store(verify).CheckAsync(id, first.Structure.Id, mark, actor with { Subject = "other" }, default));
        await Task.WhenAll(Check(first.Structure.Id, new(Guid.NewGuid(), 1, false)), Check(second.Structure.Id, new(Guid.NewGuid(), 0, true)));
        state = await Read(); Assert.Equal(1, state.Completed); Assert.Single(state.History, h => h.Action == "Uncheck");
        Assert.Equal(first.Structure.Id, state.Structures[0].Structure.Id);
        var pdf = (await verify.MeasurementReports.AsNoTracking().SingleAsync(r => r.ProjectId == id)).Pdf;
        var sourceBefore = (await verify.MeasurementChecklists.AsNoTracking().SingleAsync(c => c.ProjectId == id)).SnapshotJson;
        async IAsyncEnumerable<DocumentUpload> Upload(byte[] bytes, Guid? replaces = null, string name = "acta.pdf") { using var stream = new MemoryStream(bytes); yield return new("file-0", name, "application/pdf", replaces, stream); await Task.CompletedTask; }
        await Error("InvalidEngineeringPdf", () => Store(verify).UploadAsync(id, Guid.NewGuid(), state.Revision, Upload([1,2,3]), actor, default));
        var key = Guid.NewGuid(); var revision = state.Revision;
        await Store(verify).UploadAsync(id, key, revision, Upload(pdf), actor, default);
        await Store(verify).UploadAsync(id, key, revision, Upload(pdf), actor, default);
        state = await Read(); Assert.Single(state.Documents);
        var old = state.CurrentDocumentId!.Value;
        await Error("EngineeringIncomplete", () => Store(verify).CloseAsync(id, new(Guid.NewGuid(), state.Revision, old), actor, default));
        await Store(verify).UploadAsync(id, Guid.NewGuid(), state.Revision, Upload(pdf, old, "acta-final.pdf"), actor, default);
        state = await Read(); Assert.Equal(2, state.Documents.Count); Assert.False(state.Documents[0].Document.IsCurrent);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(pdf)), state.Documents[1].Sha256);
        foreach (var doc in state.Documents)
        {
            var download = await Store(verify).DownloadAsync(id, doc.Document.Id, new("engineer", SystemRole.Engineering), default);
            using var copy = new MemoryStream(); await download.Content.CopyToAsync(copy); await download.Content.DisposeAsync(); Assert.Equal(pdf, copy.ToArray());
            await Error("EngineeringAccessDenied", () => Store(verify).DownloadAsync(Guid.NewGuid(), doc.Document.Id, new("engineer", SystemRole.Engineering), default));
        }
        foreach (var row in state.Structures.Where(r => !r.Completed)) await Check(row.Structure.Id, new(Guid.NewGuid(), row.Revision, true));
        state = await Read(); Assert.Equal(100, state.Progress); Assert.Equal("InProgress", state.TaskStatus);
        var close = new EngineeringClose(Guid.NewGuid(), state.Revision, state.CurrentDocumentId!.Value);
        await Error("EngineeringActaRequired", () => Store(verify).CloseAsync(id, close with { DocumentId = Guid.NewGuid() }, actor, default));
        var currentFile = await verify.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == close.DocumentId);
        await storage.DeleteAsync(currentFile.StorageKey, default);
        await storage.SaveAsync(currentFile.StorageKey, new byte[] { 1, 2, 3 }, default);
        await Error("EngineeringActaIntegrity", () => Store(verify).CloseAsync(id, close, actor, default));
        Assert.Equal("InProgress", (await Read()).TaskStatus);
        await storage.DeleteAsync(currentFile.StorageKey, default);
        await storage.SaveAsync(currentFile.StorageKey, pdf, default);
        var unconfigured = new EfEngineeringStore(verify, timing, new(verify, new ProjectNotificationPlanner(timing)), storage, new IdentityDouble("engineer"));
        await Error("OpeningAdvisorNotConfigured", () => unconfigured.CloseAsync(id, close, actor, default));
        Assert.Equal("InProgress", (await Read()).TaskStatus);
        Assert.False(await verify.OpeningRecordRounds.AnyAsync(r => r.ProjectId == id));
        await Store(verify).CloseAsync(id, close, actor, default); await Store(verify).CloseAsync(id, close, actor, default);
        state = await Read(); Assert.Equal("Completed", state.TaskStatus); Assert.False(state.CanModify); Assert.NotNull(state.CompletedAt);
        await Error("EngineeringLocked", () => Check(first.Structure.Id, new(Guid.NewGuid(), state.Structures[0].Revision, false)));
        await Error("EngineeringLocked", () => Store(verify).UploadAsync(id, Guid.NewGuid(), state.Revision, Upload(pdf, state.CurrentDocumentId), actor, default));
        var final = await verify.EngineeringChecklists.AsNoTracking().SingleAsync(c => c.ProjectId == id);
        Assert.Equal("engineer", final.CompletedBySubject); Assert.Contains(state.CurrentDocumentId!.Value.ToString(), final.FinalSnapshotJson!);
        Assert.Equal(sourceBefore, (await verify.MeasurementChecklists.AsNoTracking().SingleAsync(c => c.ProjectId == id)).SnapshotJson);
        Assert.Equal(pdf, (await verify.MeasurementReports.AsNoTracking().SingleAsync(r => r.ProjectId == id)).Pdf);
        await Store(verify).InitializeHistoricalAsync(id, new(source.SubmissionId!.Value, "Explicit synthetic replay"), actor, default);
        Assert.Equal(1, await verify.EngineeringChecklists.CountAsync(c => c.ProjectId == id));
        await OpeningRecordPostgresChecks.VerifyAsync(open, id, storage, timing.UtcNow, pdf);
    }
}
