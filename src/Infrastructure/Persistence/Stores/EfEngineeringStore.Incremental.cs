using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfEngineeringStore
{
    private Task<bool> IsIncrementalAsync(Guid id, CancellationToken token) => db.MeasurementChecklists.AnyAsync(c => c.ProjectId == id && c.IsIncremental, token);
    private async Task RequireHistoricalAsync(Guid id, CancellationToken token)
    {
        if (await IsIncrementalAsync(id, token)) throw new DocumentException(409, "EngineeringLocked");
    }
    private async Task<MeasurementStructureView?> IncrementalSourceAsync(Guid id, Guid structureId, CancellationToken token)
    {
        var canonical = await db.Set<MeasurementSubmissionMember>().AsNoTracking().Where(m => m.ProjectId == id && m.StructureId == structureId && m.EvidenceVersion == 1 && db.Set<EngineeringReceipt>().Any(r => r.ProjectId == id && r.SubmissionId == m.SubmissionId)).Select(m => m.SnapshotCanonical).SingleOrDefaultAsync(token);
        return canonical == null ? null : JsonSerializer.Deserialize<MeasurementStructureView>(canonical);
    }
    private async Task<EngineeringState?> ReadIncrementalAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        var task = await db.ProjectTasks.AsNoTracking().SingleOrDefaultAsync(t => t.ProjectId == id && t.Type == ProjectTaskType.PrepareOpeningRecord, token);
        if (task == null) return null;
        var rows = await (from c in db.Set<EngineeringEvidenceCheck>().AsNoTracking()
                          join m in db.Set<MeasurementSubmissionMember>().AsNoTracking() on new { c.ProjectId, c.SubmissionId, c.StructureId, c.EvidenceVersion } equals new { m.ProjectId, m.SubmissionId, m.StructureId, m.EvidenceVersion }
                          join s in db.Set<MeasurementSubmission>().AsNoTracking() on c.SubmissionId equals s.Id
                          where c.ProjectId == id && c.EvidenceVersion == 1 orderby s.Number, m.Position
                          select new { Check = c, Member = m, Submission = s }).ToArrayAsync(token);
        var history = await db.Set<EngineeringCheckEvent>().AsNoTracking().Where(e => e.ProjectId == id).OrderBy(e => e.At).ThenBy(e => e.StructureId).ThenBy(e => e.Revision).ToArrayAsync(token);
        var completed = rows.Count(r => r.Check.Completed);
        var workspace=await WorkspaceView(id,scope,token);
        var isTeam=scope.Role==SystemRole.Engineering && engineering.Subjects.Contains(scope.Subject,StringComparer.Ordinal);
        EngineeringWork Work(Guid structureId,string? changedBy) {
            var package=workspace.Packages.Single(p=>p.Structures.Any(m=>m.StructureId==structureId));
            var acta=workspace.Actas.SingleOrDefault(a=>a.Structures.Any(m=>m.StructureId==structureId));
            return new(package.Id,package.ResponsibleSubject,isTeam && package.ResponsibleSubject==scope.Subject && package.Status=="Open" && acta?.Status!="Confirmed",changedBy,acta?.Id);
        }
        var canModify=rows.Any(r=>Work(r.Check.StructureId,r.Check.ChangedBySubject).CanModify);
        // Reuse the same bounded-query delivery projection without opening a nested read transaction.
        var deliveries = await new EfMeasurementStore(db, timing, notifications, files, engineering).ReadDeliveriesAsync(id, token);
        return new(id, null, null, task.Id, task.Status.ToString(), task.AssignedToGoogleSubject!, canModify, task.StartedAt!.Value, null,
            history.LongLength, rows.Length, completed, rows.Length - completed, rows.Length == 0 ? 0 : decimal.Round(100m * completed / rows.Length, 2),
            rows.Select(r => new EngineeringRow(JsonSerializer.Deserialize<MeasurementStructureView>(r.Member.SnapshotCanonical)!, r.Check.Revision, r.Check.Completed, r.Check.ChangedAt, new(r.Submission.Id, r.Submission.Number, r.Check.EvidenceVersion), Work(r.Check.StructureId,r.Check.ChangedBySubject))).ToArray(), [], null,
            history.Select(e => new EngineeringHistory(e.Completed ? "Check" : "Uncheck", e.StructureId, e.At, new(e.ActorEmail, e.ActorName), e.Revision, null)).ToArray(), true, deliveries, workspace);
    }
    private async Task CheckIncrementalAsync(Guid id, Guid structureId, EngineeringCheck command, DecisionActor actor, CancellationToken token)
    {
        var tasks = await LockProject(id, token);
        var task = tasks.SingleOrDefault(t => t.Type == ProjectTaskType.PrepareOpeningRecord);
        if (task == null || !engineering.Subjects.Contains(actor.Subject,StringComparer.Ordinal)) throw new DocumentException(403, "EngineeringAccessDenied");
        if (task.Status != ProjectTaskStatus.InProgress) throw new DocumentException(409, "EngineeringLocked");
        var row = await db.Set<EngineeringEvidenceCheck>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id && c.StructureId == structureId && c.EvidenceVersion == 1, token) ?? throw new DocumentException(404, "EngineeringStructureNotFound");
        if (!await (from m in db.Set<EngineeringAssignedStructure>() join a in db.Set<WorkPackageAssignment>() on m.AssignmentId equals a.Id join p in db.Set<EngineeringPackage>() on a.PackageId equals p.Id
            where m.ProjectId==id && m.StructureId==structureId && m.SubmissionId==row.SubmissionId && a.Subject==actor.Subject && a.EndedAt==null && p.Status=="Open" select m).AnyAsync(token)) throw new DocumentException(403,"EngineeringAccessDenied");
        var fingerprint = Fingerprint(new { structureId, command });
        var replay = await db.Set<EngineeringCheckEvent>().AsNoTracking().SingleOrDefaultAsync(e => e.ProjectId == id && e.RequestKey == command.RequestKey, token);
        if (replay != null) { if (replay.Fingerprint != fingerprint || replay.ActorSubject != actor.Subject) throw new DocumentException(409, "EngineeringRequestConflict"); return; }
        if(await (from m in db.Set<ActiveOpeningRecordMember>() join a in db.Set<EngineeringOpeningState>() on m.OpeningRecordId equals a.OpeningRecordId where m.ProjectId==id && m.StructureId==structureId && a.ConfirmedAt!=null select m).AnyAsync(token)) throw new DocumentException(409,"EngineeringLocked");
        Revision(row.Revision, command.ExpectedRevision);
        db.Attach(row); row.SetCompleted(command.Completed, timing.UtcNow, actor.Subject);
        var audit = new EngineeringCheckEvent(row, command.RequestKey, fingerprint, actor.Email, actor.Name);
        try { db.Add(audit); await db.SaveChangesAsync(token); }
        finally { db.Entry(row).State = EntityState.Detached; db.Entry(audit).State = EntityState.Detached; }
    }
}
