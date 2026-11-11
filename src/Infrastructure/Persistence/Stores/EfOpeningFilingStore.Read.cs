using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfOpeningFilingStore
{
    public async Task<OpeningFilingsView?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await CanRead(id, scope, token)) return null;
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == id, token);
        var roots = await (from d in db.Set<OpeningFilingDelivery>().AsNoTracking() join a in db.Set<OpeningAcceptance>().AsNoTracking() on d.AcceptanceId equals a.Id
            join acta in db.Set<OpeningRecord>().AsNoTracking() on a.OpeningRecordId equals acta.Id where d.ProjectId == id orderby a.At, d.Id select new { d, a, acta.Number }).ToArrayAsync(token);
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id).ToArrayAsync(token);
        var states = await db.Set<EngineeringOpeningState>().AsNoTracking().Where(a => a.ProjectId == id).ToArrayAsync(token);
        var workspaces = await db.Set<OpeningFilingWorkspace>().AsNoTracking().Where(w => w.ProjectId == id).ToArrayAsync(token);
        var versions = await db.Set<OpeningFilingVersion>().AsNoTracking().Where(v => v.ProjectId == id).OrderBy(v => v.Number).ToArrayAsync(token);
        var members = await db.Set<OpeningFilingMember>().AsNoTracking().Where(m => m.ProjectId == id).ToArrayAsync(token);
        var docs = await (from l in db.Set<OpeningFilingDocument>().AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on l.DocumentId equals d.Id where l.ProjectId == id orderby d.UploadedAt select new { l, d }).ToArrayAsync(token);
        var accepted = await db.Set<OpeningAcceptanceMember>().AsNoTracking().Where(a => a.ProjectId == id).ToArrayAsync(token);
        var evidence = await db.Set<CommercialOpeningAcceptanceEvidence>().AsNoTracking().Where(a => a.ProjectId == id).ToArrayAsync(token);
        var original = await (from m in db.Set<OpeningRecordMember>().AsNoTracking() join s in db.Set<MeasurementSubmissionMember>().AsNoTracking() on new { m.SubmissionId, m.StructureId, m.EvidenceVersion } equals new { s.SubmissionId, s.StructureId, s.EvidenceVersion } where m.ProjectId == id select new { m.OpeningRecordId, m.StructureId, m.SubmissionId, m.EvidenceVersion, s.SnapshotCanonical }).ToArrayAsync(token);
        var events = await db.Set<OpeningFilingEvent>().AsNoTracking().Where(e => e.ProjectId == id).OrderBy(e => e.At).ThenBy(e => e.RequestKey).ToArrayAsync(token);
        return new(project.Name, project.SgCode, roots.Select(x => {
            var w = workspaces.SingleOrDefault(w => w.FilingId == x.d.Id); var task = tasks.SingleOrDefault(t => t.Id == x.d.TaskId);
            var actor = task?.AssignedToGoogleSubject ?? ""; var source = accepted.Where(m => m.AcceptanceId == x.a.Id).Select(m => m.StructureId).ToHashSet();
            var complete = !x.a.IsLegacy && source.Count > 0 && task?.IncrementalAcceptanceId == x.a.Id && states.Any(s => s.OpeningRecordId == x.a.OpeningRecordId && s.ConfirmedAt != null && s.ResponsibleSubject == actor) && source.SetEquals(evidence.Where(e => e.AcceptanceId == x.a.Id && e.Attested && e.SignedDocumentId == x.a.SignedDocumentId).Select(e => e.StructureId));
            var vv = versions.Where(v => v.FilingId == x.d.Id).ToArray();
            var status = w == null ? "Pending" : vv.Any(v => v.ConfirmedAt == null) ? "Preparing" : "Confirmed";
            return new OpeningFilingView(x.d.Id, x.d.TaskId, x.a.Id, x.a.OpeningRecordId, x.Number, x.a.PresentedDocumentId, x.a.SignedDocumentId, x.a.At, actor, status, w?.NumberOriginal, w?.Revision ?? 0,
                complete && scope.Role == SystemRole.Engineering && scope.Subject == actor && engineering.Subjects.Contains(actor, StringComparer.Ordinal), complete ? null : "LEGACY_INCOMPLETE_EVIDENCE",
                original.Where(m => m.OpeningRecordId == x.a.OpeningRecordId && source.Contains(m.StructureId)).Select(m => new OpeningFilingSource(m.StructureId, m.SubmissionId, m.EvidenceVersion, JsonSerializer.Deserialize<MeasurementStructureView>(m.SnapshotCanonical)!)).ToArray(),
                vv.Select(v => new OpeningFilingVersionView(v.Id, v.Number, v.Reason, v.CreatedAt, v.CreatedBySubject, v.ConfirmedAt, v.ConfirmedBySubject, v.ManifestSha256,
                    members.Where(m => m.VersionId == v.Id).Select(m => new OpeningFilingOp(m.StructureId, m.OpOriginal, m.Confirmed)).ToArray(),
                    docs.Where(d => d.l.VersionId == v.Id).Select(d => new OpeningFilingDocumentView(d.d.Id, d.l.Category, d.l.FileVersion, d.l.State, d.d.OriginalFileName, d.d.SizeBytes, d.l.Sha256, d.d.UploadedAt, d.d.UploadedByGoogleSubject)).ToArray())).ToArray(),
                events.Where(e => e.FilingId == x.d.Id).Select(e => new OpeningFilingHistory(e.Action, e.At, e.ActorSubject)).ToArray());
        }).ToArray());
    }
}
