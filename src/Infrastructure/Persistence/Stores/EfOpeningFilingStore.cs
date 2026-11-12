using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed partial class EfOpeningFilingStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications,
    IStreamingFileStorage files, IEngineeringAssigneeIdentity engineering, Microsoft.Extensions.Options.IOptions<OpeningFilingLimits> limits) : IOpeningFilingStore
{
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Fingerprint(object value) => Hash(Encoding.UTF8.GetBytes(Json(value)));
    private static DocumentException Conflict(string code = "OpeningFilingConflict") => new(409, code);
    private Task<bool> CanRead(Guid id, ProjectReadScope scope, CancellationToken token) => db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token);
    private async Task<(OpeningFilingDelivery Delivery, OpeningAcceptance Acceptance, ProjectTask Task)> Source(Guid id, Guid filingId, CancellationToken token)
    {
        var delivery = await db.Set<OpeningFilingDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == filingId && x.ProjectId == id, token) ?? throw new DocumentException(404, "OpeningFilingNotFound");
        var acceptance = await db.Set<OpeningAcceptance>().AsNoTracking().SingleAsync(x => x.Id == delivery.AcceptanceId && x.ProjectId == id, token);
        var task = await db.ProjectTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.TaskId && x.ProjectId == id && x.Type == ProjectTaskType.FileOpeningRecord, token) ?? throw Conflict("OpeningFilingEvidenceIncomplete");
        return (delivery, acceptance, task);
    }
    private async Task Authorize(Guid id, OpeningAcceptance acceptance, ProjectTask task, DecisionActor actor, CancellationToken token)
    {
        if (!engineering.Subjects.Contains(actor.Subject, StringComparer.Ordinal) || task.AssignedToGoogleSubject != actor.Subject ||
            !await CanRead(id, new(actor.Subject, SystemRole.Engineering), token)) throw new DocumentException(403, "EngineeringAccessDenied");
        if (acceptance.IsLegacy || task.IncrementalAcceptanceId != acceptance.Id ||
            !await db.Set<EngineeringOpeningState>().AnyAsync(s => s.ProjectId == id && s.OpeningRecordId == acceptance.OpeningRecordId && s.ConfirmedAt != null && s.ResponsibleSubject == actor.Subject, token))
            throw Conflict("OpeningFilingEvidenceIncomplete");
        var members = await db.Set<OpeningAcceptanceMember>().Where(m => m.AcceptanceId == acceptance.Id && m.ProjectId == id).Select(m => m.StructureId).ToArrayAsync(token);
        var evidence = await db.Set<CommercialOpeningAcceptanceEvidence>().Where(e => e.AcceptanceId == acceptance.Id && e.ProjectId == id && e.SignedDocumentId == acceptance.SignedDocumentId && e.Attested).Select(e => e.StructureId).ToArrayAsync(token);
        if (members.Length == 0 || !members.ToHashSet().SetEquals(evidence)) throw Conflict("OpeningFilingEvidenceIncomplete");
    }
    private async Task Lock(Guid id, CancellationToken token)
    {
        if (!await db.Projects.FromSqlInterpolated($"""SELECT * FROM "Projects" WHERE "Id"={id} FOR UPDATE""").AsNoTracking().AnyAsync(token)) throw new DocumentException(404, "ProjectNotFound");
        await db.ProjectTasks.FromSqlInterpolated($"""SELECT * FROM "ProjectTasks" WHERE "ProjectId"={id} FOR UPDATE""").AsNoTracking().ToArrayAsync(token);
    }
    private async Task<bool> Replay(Guid id, Guid filingId, Guid key, string fp, DecisionActor actor, CancellationToken token)
    {
        var old = await db.Set<OpeningFilingEvent>().AsNoTracking().SingleOrDefaultAsync(e => e.ProjectId == id && e.RequestKey == key, token);
        if (old == null) return false;
        if (old.FilingId != filingId || old.Fingerprint != fp || old.ActorSubject != actor.Subject) throw Conflict("OpeningFilingRequestConflict");
        return true;
    }
    private static void Revision(long actual, long expected) { if (actual != expected) throw Conflict("OpeningFilingRevisionConflict"); }
    private async Task<(OpeningFilingWorkspace Workspace, OpeningFilingVersion Version)> Draft(OpeningFilingDelivery delivery, ProjectTask task, DecisionActor actor, long revision, CancellationToken token)
    {
        var workspace = await db.Set<OpeningFilingWorkspace>().SingleOrDefaultAsync(w => w.FilingId == delivery.Id, token);
        if (workspace == null)
        {
            Revision(0, revision);
            if (task.Status != ProjectTaskStatus.Pending) throw Conflict("OpeningFilingEvidenceIncomplete");
            workspace = new(delivery, actor.Subject, timing.UtcNow); db.Add(workspace);
            var first = new OpeningFilingVersion(workspace, 1, null, timing.UtcNow, actor.Subject); db.Add(first);
            return (workspace, first);
        }
        Revision(workspace.Revision, revision);
        if (workspace.ResponsibleSubject != actor.Subject) throw new DocumentException(403, "EngineeringAccessDenied");
        var version = await db.Set<OpeningFilingVersion>().SingleOrDefaultAsync(v => v.FilingId == delivery.Id && v.ConfirmedAt == null, token) ?? throw Conflict("OpeningFilingLocked");
        return (workspace, version);
    }
    public async Task CommandAsync(Guid id, Guid filingId, OpeningFilingCommand c, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token); await Lock(id, token);
        var source = await Source(id, filingId, token); await Authorize(id, source.Acceptance, source.Task, actor, token);
        var fp = Fingerprint(c); if (await Replay(id, filingId, c.RequestKey, fp, actor, token)) return;
        var at = timing.UtcNow;
        if (c.Action == "Amend")
        {
            var w = await db.Set<OpeningFilingWorkspace>().SingleOrDefaultAsync(w => w.FilingId == filingId, token) ?? throw Conflict();
            Revision(w.Revision, c.ExpectedRevision);
            if (w.ResponsibleSubject != actor.Subject || w.FirstConfirmedAt == null || source.Task.Status != ProjectTaskStatus.Completed || await db.Set<OpeningFilingVersion>().AnyAsync(v => v.FilingId == filingId && v.ConfirmedAt == null, token)) throw Conflict();
            var previous = await db.Set<OpeningFilingVersion>().Where(v => v.FilingId == filingId).OrderByDescending(v => v.Number).FirstAsync(token);
            var next = new OpeningFilingVersion(w, previous.Number + 1, c.Reason, at, actor.Subject); db.Add(next);
            var members = await db.Set<OpeningFilingMember>().AsNoTracking().Where(m => m.VersionId == previous.Id).ToArrayAsync(token);
            db.AddRange(members.Select(m => new OpeningFilingMember(next, m.StructureId, m.OpOriginal, m.Confirmed)));
            var docs = await db.Set<OpeningFilingDocument>().AsNoTracking().Where(d => d.VersionId == previous.Id && d.State == "Current").ToArrayAsync(token);
            db.AddRange(docs.Select(d => new OpeningFilingDocument(next, d.DocumentId, d.Category, d.Sha256, d.LineageId, d.FileVersion))); w.Advance();
        }
        else
        {
            var (w, v) = await Draft(source.Delivery, source.Task, actor, c.ExpectedRevision, token);
            if (c.Action == "Save")
            {
                var accepted = await db.Set<OpeningAcceptanceMember>().Where(m => m.AcceptanceId == source.Acceptance.Id).Select(m => m.StructureId).ToArrayAsync(token);
                var selected = c.Members ?? throw new DocumentException(400, "InvalidOpeningFilingCommand");
                if (selected.Select(m => m.StructureId).Distinct().Count() != selected.Count || selected.Any(m => !accepted.Contains(m.StructureId))) throw Conflict("OpeningFilingScopeConflict");
                OpeningFilingMember[] members;
                try { members = selected.Select(m => new OpeningFilingMember(v, m.StructureId, m.Code, m.Confirmed)).ToArray(); }
                catch (ArgumentException) { throw new DocumentException(400, "InvalidOpeningFilingCommand"); }
                if (members.Select(m => m.OpNormalized).Distinct(StringComparer.Ordinal).Count() != members.Length) throw Conflict("OpeningFilingOpConflict");
                if (w.FirstConfirmedAt == null)
                {
                    string normalized; try { normalized = OpeningFilingRules.NormalizeIdentifier(c.Number!); } catch (ArgumentException) { throw new DocumentException(400, "InvalidOpeningFilingCommand"); }
                    if (await db.Set<OpeningFilingWorkspace>().AnyAsync(x => x.ProjectId == id && x.FilingId != filingId && x.NumberNormalized == normalized, token)) throw Conflict("OpeningFilingNumberConflict");
                    w.SetNumber(c.Number!);
                }
                else { if (c.Number != w.NumberOriginal) throw Conflict("OpeningFilingLocked"); w.Advance(); }
                db.RemoveRange(await db.Set<OpeningFilingMember>().Where(m => m.VersionId == v.Id).ToArrayAsync(token));
                await db.SaveChangesAsync(token); db.AddRange(members);
            }
            else if (c.Action == "Withdraw")
            {
                var doc = await db.Set<OpeningFilingDocument>().SingleOrDefaultAsync(d => d.VersionId == v.Id && d.DocumentId == c.DocumentId && d.State == "Current", token) ?? throw Conflict();
                doc.Retire(v, false); w.Advance();
            }
            else if (c.Action == "Confirm") await Confirm(source.Acceptance, source.Task, w, v, actor, token);
            else throw new DocumentException(400, "InvalidOpeningFilingCommand");
        }
        db.Add(new OpeningFilingEvent(id, filingId, c.RequestKey, fp, c.Action, at, actor.Subject, Json(c)));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    private async Task Confirm(OpeningAcceptance acceptance, ProjectTask task, OpeningFilingWorkspace w, OpeningFilingVersion v, DecisionActor actor, CancellationToken token)
    {
        if (w.NumberNormalized == null) throw Conflict("OpeningFilingIncomplete");
        var members = await db.Set<OpeningFilingMember>().AsNoTracking().Where(m => m.VersionId == v.Id).OrderBy(m => m.StructureId).ToArrayAsync(token);
        var accepted = await db.Set<OpeningAcceptanceMember>().Where(m => m.AcceptanceId == acceptance.Id).Select(m => m.StructureId).ToArrayAsync(token);
        if (members.Length == 0 || members.Any(m => !m.Confirmed) || !accepted.ToHashSet().SetEquals(members.Select(m => m.StructureId))) throw Conflict("OpeningFilingIncomplete");
        var docs = await (from link in db.Set<OpeningFilingDocument>().AsNoTracking() join doc in db.ProjectDocuments.AsNoTracking() on link.DocumentId equals doc.Id where link.VersionId == v.Id && link.State == "Current" && doc.ProjectId == w.ProjectId select new { link, doc }).OrderBy(x => x.link.Category).ThenBy(x => x.doc.Id).ToArrayAsync(token);
        if (OpeningFilingRules.Categories.Take(7).Any(c => !docs.Any(d => d.link.Category == c))) throw Conflict("OpeningFilingIncomplete");
        foreach (var d in docs) await Verified(d.doc, d.link.Sha256, token, d.link.Category != "Additional");
        foreach (var documentId in new[] { acceptance.PresentedDocumentId, acceptance.SignedDocumentId }.Distinct())
        {
            var scope = await db.Set<OpeningDocumentScope>().AsNoTracking().SingleOrDefaultAsync(s => s.DocumentId == documentId && s.ProjectId == w.ProjectId && s.OpeningRecordId == acceptance.OpeningRecordId, token) ?? throw Conflict("OpeningFilingEvidenceIncomplete");
            var doc = await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId && d.ProjectId == w.ProjectId, token); await Verified(doc, scope.Sha256, token);
        }
        var evidence = await db.Set<CommercialOpeningAcceptanceEvidence>().AsNoTracking().Where(e => e.AcceptanceId == acceptance.Id).OrderBy(e => e.StructureId).ToArrayAsync(token);
        var at = timing.UtcNow;
        var manifest = Json(new { w.ProjectId, w.FilingId, AcceptanceId = acceptance.Id, acceptance.OpeningRecordId, acceptance.PresentedDocumentId, acceptance.SignedDocumentId,
            VersionId = v.Id, v.Number, w.NumberOriginal, w.NumberNormalized, w.ResponsibleSubject, Structures = members, Evidence = evidence,
            Documents = docs.Select(d => new { d.doc.Id, d.doc.OriginalFileName, d.doc.ContentType, d.doc.SizeBytes, d.link.Category, d.link.FileVersion, d.link.Sha256 }), At = at, Actor = actor.Subject, Consumers = new[] { "Production", "Logistics" } });
        if (v.Number == 1)
        {
            if (task.Status is not (ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress)) throw Conflict("OpeningFilingLocked");
            db.Attach(task); if (task.Status == ProjectTaskStatus.Pending) task.StartUntracked(at); task.Complete(at);
            await notifications.StageAsync([task], at, token);
        }
        else if (task.Status != ProjectTaskStatus.Completed) throw Conflict();
        v.Confirm(at, actor.Subject, manifest); w.Confirm(at); db.Add(new OpeningFilingRelease(v));
    }
    private async Task<byte[]> Verified(ProjectDocument doc, string hash, CancellationToken token, bool pdf = true)
    {
        try
        {
        await using var stream = await files.OpenReadAsync(doc.StorageKey, token);
        var bytes = await EfEngineeringStore.ReadBounded(stream, token);
        if (bytes.LongLength != doc.SizeBytes || Hash(bytes) != hash) throw Conflict("OpeningFilingDocumentInvalid");
        if (pdf) EfEngineeringStore.ValidatePdf(doc.OriginalFileName, bytes); return bytes;
        }
        catch (FileNotFoundException) { throw Conflict("OpeningFilingDocumentInvalid"); }
        catch (DirectoryNotFoundException) { throw Conflict("OpeningFilingDocumentInvalid"); }
    }
}
