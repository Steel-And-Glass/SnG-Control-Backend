using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Persistence;

internal static class OpeningRecordPostgresChecks
{
    public static async Task VerifyAsync(Func<SnGControlDbContext> open, Guid id, IStreamingFileStorage files, DateTime now, byte[] pdf)
    {
        var clock = new ControlledTimeProvider(new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)); var timing = new TaskTiming(clock, new());
        EfOpeningRecordStore Store(SnGControlDbContext db) => new(db, timing, new(db, new ProjectNotificationPlanner(timing)), files, new IdentityDouble("engineer"), new OpeningRoles());
        async Task<OpeningState> Read() { await using var db = open(); return (await Store(db).ReadAsync(id, new("advisor", SystemRole.CommercialAdvisor), default))!; }
        async Task Send(Guid roundId, OpeningCommand c, SystemRole role = SystemRole.CommercialAdvisor, string? subject = null)
        { await using var db = open(); await Store(db).CommandAsync(id, roundId, c, new(subject ?? (role == SystemRole.Engineering ? "engineer" : "advisor"), null, "Synthetic actor"), role, default); }
        static OpeningCommand Cmd(OpeningRoundView r, string action) => new(Guid.NewGuid(), r.Revision, action);
        static async Task Error(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<DocumentException>(action)).Code);
        async IAsyncEnumerable<DocumentUpload> Body(Guid? previous) { using var stream = new MemoryStream(pdf); yield return new("file-0", "synthetic-acta.pdf", "application/pdf", previous, stream); await Task.CompletedTask; }
        async Task Upload(OpeningRoundView r, OpeningDocumentKind kind, Guid key)
        {
            await using var db = open(); var review = kind == OpeningDocumentKind.Revised;
            await Store(db).UploadAsync(id, r.Id, key, r.Revision, kind, Body(review ? r.RevisedDocumentId : r.SignedDocumentId), new(review ? "engineer" : "advisor", null, "Synthetic actor"), review ? SystemRole.Engineering : SystemRole.CommercialAdvisor, default);
        }
        var state = await Read(); var first = Assert.Single(state.Rounds); var ids = state.Structures.Select(s => s.Id).ToArray();
        Assert.Equal("Ready", first.Status); Assert.Null(first.ResponseSeconds); Assert.Null(first.PresentedAt);
        await using var verify = open();
        var original = await verify.EngineeringChecklists.AsNoTracking().SingleAsync(c => c.ProjectId == id); var frozen = original.FinalSnapshotJson;
        var originalRows = await verify.EngineeringStructures.AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Position).ToArrayAsync();
        Assert.Single(await verify.ProjectNotifications.Where(n => n.ProjectTaskId == first.PresentationTaskId).ToArrayAsync());
        Assert.Null((await verify.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == first.PresentationTaskId)).DeadlineAt);
        await Error("OpeningAccessDenied", () => Send(first.Id, Cmd(first, "Present"), SystemRole.Management));
        await Error("OpeningAccessDenied", () => Send(first.Id, Cmd(first, "Present"), subject: "other-advisor"));
        await Error("OpeningRoundStateConflict", () => Send(first.Id, Cmd(first, "ConfirmStructure") with { StructureId = ids[0], Value = true }));
        var present = Cmd(first, "Present"); await Task.WhenAll(Send(first.Id, present), Send(first.Id, present));
        await Error("OpeningRequestConflict", () => Send(first.Id, present with { Action = "Accept" }));
        state = await Read(); first = state.Rounds[0]; Assert.Equal(0, first.ResponseSeconds);
        await Error("OpeningApprovalsIncomplete", () => Send(first.Id, Cmd(first, "Accept") with { AttestSignedForPresentedVersion = true }));
        var signedOldKey = Guid.NewGuid(); await Upload(first, OpeningDocumentKind.Signed, signedOldKey); await Upload(first, OpeningDocumentKind.Signed, signedOldKey);
        state = await Read(); var oldSigned = state.Rounds[0].SignedDocumentId; Assert.Single(state.Documents, d => d.Kind == "Signed");
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var r = state.Rounds.Last();
            if (r.Status == "Ready") { await Send(r.Id, Cmd(r, "Present")); state = await Read(); r = state.Rounds.Last(); }
            clock.Now = clock.Now.AddMinutes(cycle == 0 ? 7 : 12);
            var selected = cycle == 0 ? ids.Take(2).ToArray() : [ids[0]];
            var changes = Cmd(r, "RequestChanges") with { Changes = selected.Select(x => new OpeningChange(x, "Individual synthetic correction")).ToArray(), Observations = "General synthetic instructions" };
            await Task.WhenAll(Send(r.Id, changes), Send(r.Id, changes));
            state = await Read(); r = state.Rounds.Last(); Assert.Equal("ChangesRequested", r.Status); Assert.Equal(cycle == 0 ? 420 : 720, r.ResponseSeconds);
            Assert.All(r.Structures.Where(s => !selected.Contains(s.StructureId)), s => Assert.Equal("Confirmed", s.Approval));
            Assert.Equal(selected.Length, r.Structures.Count(s => s.Approval == "ChangeRequested"));
            Assert.Single(await verify.ProjectTasks.AsNoTracking().Where(t => t.Id == r.ReviewTaskId).ToArrayAsync());
            await Error("OpeningAffectedStructuresRequired", () => Upload(r, OpeningDocumentKind.Revised, Guid.NewGuid()));
            var affected = cycle == 0 ? selected : selected.Append(ids[2]).ToArray();
            await Send(r.Id, Cmd(r, "DeclareAffected") with { AffectedStructureIds = affected }, SystemRole.Engineering);
            state = await Read(); r = state.Rounds.Last();
            await Error("OpeningReviewIncomplete", () => Send(r.Id, Cmd(r, "CloseReview"), SystemRole.Engineering));
            var uploadKey = Guid.NewGuid(); await Upload(r, OpeningDocumentKind.Revised, uploadKey); await Upload(r, OpeningDocumentKind.Revised, uploadKey);
            state = await Read(); r = state.Rounds.Last();
            foreach (var row in r.Structures.Where(s => s.Affected))
            {
                var check = Cmd(r, "ReviewCheck") with { StructureId = row.StructureId, ExpectedRevision = row.Revision, Value = true };
                await Task.WhenAll(Send(r.Id, check, SystemRole.Engineering), Send(r.Id, check, SystemRole.Engineering));
            }
            state = await Read(); r = state.Rounds.Last();
            var uncheckRow = r.Structures.First(s => s.Affected);
            await Send(r.Id, Cmd(r, "ReviewCheck") with { StructureId = uncheckRow.StructureId, ExpectedRevision = uncheckRow.Revision, Value = false }, SystemRole.Engineering);
            state = await Read(); r = state.Rounds.Last();
            await Error("OpeningReviewIncomplete", () => Send(r.Id, Cmd(r, "CloseReview") with { DocumentId = r.RevisedDocumentId }, SystemRole.Engineering));
            await Send(r.Id, Cmd(r, "ReviewCheck") with { StructureId = uncheckRow.StructureId, ExpectedRevision = r.Structures.Single(s => s.StructureId == uncheckRow.StructureId).Revision, Value = true }, SystemRole.Engineering);
            state = await Read(); r = state.Rounds.Last();
            var close = Cmd(r, "CloseReview") with { DocumentId = r.RevisedDocumentId };
            if (cycle == 0)
            {
                await using var failed = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(verify.Database.GetConnectionString()).AddInterceptors(new RejectSave(2)).Options);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).CommandAsync(id, r.Id, close, new("engineer", null, null), SystemRole.Engineering, default));
                Assert.Equal("ChangesRequested", (await Read()).Rounds.Last().Status);
                Assert.Equal(1, await verify.OpeningRecordRounds.CountAsync(x => x.ProjectId == id));
            }
            await Task.WhenAll(Send(r.Id, close, SystemRole.Engineering), Send(r.Id, close, SystemRole.Engineering));
            state = await Read(); var next = state.Rounds.Last(); Assert.Equal(cycle + 2, next.Number); Assert.Equal("Ready", next.Status); Assert.Null(next.ResponseSeconds);
            Assert.Equal(ids, next.Structures.Select(s => s.StructureId));
            Assert.All(next.Structures.Where(s => affected.Contains(s.StructureId)), s => Assert.Equal("AwaitingApproval", s.Approval));
            Assert.All(next.Structures.Where(s => !affected.Contains(s.StructureId)), s => { Assert.Equal("Confirmed", s.Approval); Assert.True(s.CarriedApproval); });
        }
        var last = state.Rounds.Last(); await Send(last.Id, Cmd(last, "Present")); state = await Read(); last = state.Rounds.Last();
        foreach (var row in last.Structures.Where(s => s.Approval == "AwaitingApproval")) await Send(last.Id, Cmd(last, "ConfirmStructure") with { StructureId = row.StructureId, ExpectedRevision = row.Revision, Value = true });
        state = await Read(); last = state.Rounds.Last();
        await Error("OpeningSignedActaRequired", () => Send(last.Id, Cmd(last, "Accept") with { DocumentId = oldSigned, AttestSignedForPresentedVersion = true }));
        await Upload(last, OpeningDocumentKind.Signed, Guid.NewGuid()); state = await Read(); last = state.Rounds.Last();
        await Error("OpeningSignedActaRequired", () => Send(last.Id, Cmd(last, "Accept") with { DocumentId = last.SignedDocumentId }));
        await Error("OpeningSignedActaRequired", () => Send(last.Id, Cmd(last, "Accept") with { DocumentId = oldSigned, AttestSignedForPresentedVersion = true }));
        clock.Now = clock.Now.AddHours(1);
        var accept = Cmd(last, "Accept") with { DocumentId = last.SignedDocumentId, AttestSignedForPresentedVersion = true };
        await using (var failed = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(verify.Database.GetConnectionString()).AddInterceptors(new RejectSave()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).CommandAsync(id, last.Id, accept, new("advisor", null, null), SystemRole.CommercialAdvisor, default));
        Assert.Equal("Presented", (await Read()).Rounds.Last().Status);
        Assert.False(await verify.ProjectTasks.AnyAsync(t => t.ProjectId == id && t.Type == ProjectTaskType.FileOpeningRecord));
        await Task.WhenAll(Send(last.Id, accept), Send(last.Id, accept)); state = await Read(); last = state.Rounds.Last();
        Assert.Equal("Accepted", last.Status); Assert.Equal(new decimal?[] {420,720,3600}, state.Rounds.Select(r => r.ResponseSeconds));
        var filing = Assert.Single(await verify.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id && t.Type == ProjectTaskType.FileOpeningRecord).ToArrayAsync());
        Assert.Equal(last.FilingTaskId, filing.Id); Assert.Equal(ProjectTaskStatus.Pending, filing.Status); Assert.Null(filing.StartedAt); Assert.Null(filing.DeadlineAt);
        Assert.Single(await verify.ProjectNotifications.Where(n => n.ProjectTaskId == filing.Id).ToArrayAsync());
        await Error("OpeningRoundStateConflict", () => Send(last.Id, accept with { RequestKey = Guid.NewGuid() }));
        await Error("OpeningRoundStateConflict", () => Upload(last, OpeningDocumentKind.Signed, Guid.NewGuid()));
        Assert.Equal(frozen, (await verify.EngineeringChecklists.AsNoTracking().SingleAsync(c => c.Id == original.Id)).FinalSnapshotJson);
        var unchanged = await verify.EngineeringStructures.AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Position).ToArrayAsync();
        Assert.Equal(originalRows.Select(s => (s.StructureId,s.Completed,s.Revision,s.SnapshotJson)), unchanged.Select(s => (s.StructureId,s.Completed,s.Revision,s.SnapshotJson)));
        Assert.Equal(5, state.Documents.Count);
        foreach (var document in state.Documents)
        {
            var download = await Store(verify).DownloadAsync(id, document.Document.Id, new("advisor", SystemRole.CommercialAdvisor), default);
            using var bytes = new MemoryStream(); await download.Content.CopyToAsync(bytes); await download.Content.DisposeAsync(); Assert.Equal(pdf, bytes.ToArray());
        }
        var snapshot = (await verify.OpeningRecordRounds.AsNoTracking().SingleAsync(r => r.Id == last.Id)).FinalSnapshotJson!;
        Assert.Contains(last.SignedDocumentId!.Value.ToString(), snapshot); Assert.Contains(state.SubmissionId.ToString(), snapshot);
    }
    private sealed class RejectSave(int at = 1) : SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { if (++calls == at) throw new InvalidOperationException("Synthetic failure before commit"); return ValueTask.FromResult(result); }
    }
}
