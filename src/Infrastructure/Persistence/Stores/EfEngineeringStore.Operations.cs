using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfEngineeringStore
{
    private static EngineeringReference Ref(OpeningRecordMember m)=>new(m.StructureId,m.SubmissionId,m.EvidenceVersion);
    private static string ScopeHash(IEnumerable<OpeningRecordMember> members)=>Fingerprint(members.OrderBy(m=>m.StructureId).Select(Ref).ToArray());
    private async Task RequireNative(Guid id,DecisionActor actor,CancellationToken token)
    {
        if(!engineering.Subjects.Contains(actor.Subject,StringComparer.Ordinal) || !await CanRead(id,new(actor.Subject,SystemRole.Engineering),token)) throw new DocumentException(403,"EngineeringAccessDenied");
        if(!await IsIncrementalAsync(id,token)) throw new DocumentException(409,"EngineeringLocked");
    }
    private async Task<EngineeringWorkspaceEvent?> WorkspaceReplay(Guid id,Guid key,string fingerprint,DecisionActor actor,CancellationToken token)
    {
        var old=await db.Set<EngineeringWorkspaceEvent>().AsNoTracking().SingleOrDefaultAsync(e=>e.ProjectId==id && e.RequestKey==key,token);
        if(old!=null && (old.Fingerprint!=fingerprint || old.ActorSubject!=actor.Subject)) throw new DocumentException(409,"EngineeringRequestConflict");
        return old;
    }
    private async Task<EngineeringOpeningState> OwnedActa(Guid id,Guid acta,DecisionActor actor,CancellationToken token)
    {
        var state=await db.Set<EngineeringOpeningState>().AsNoTracking().SingleOrDefaultAsync(s=>s.ProjectId==id && s.OpeningRecordId==acta,token) ?? throw new DocumentException(404,"EngineeringActaNotFound");
        if(state.ResponsibleSubject!=actor.Subject) throw new DocumentException(403,"EngineeringAccessDenied");
        var ids=await db.Set<OpeningRecordMember>().Where(m=>m.ProjectId==id && m.OpeningRecordId==acta).Select(m=>m.StructureId).ToArrayAsync(token);
        var owned=await (from m in db.Set<EngineeringAssignedStructure>() join a in db.Set<WorkPackageAssignment>() on m.AssignmentId equals a.Id where m.ProjectId==id && ids.Contains(m.StructureId) && a.Subject==actor.Subject select m.StructureId).CountAsync(token);
        if(owned!=ids.Length) throw new DocumentException(403,"EngineeringAccessDenied");
        return state;
    }
    private async Task<MeasurementSubmissionMember[]> Selected(Guid id,IReadOnlyList<EngineeringReference> refs,DecisionActor actor,bool coordinator,CancellationToken token)
    {
        var ids=refs.Select(r=>r.StructureId).ToArray();
        var rows=await (from m in db.Set<MeasurementSubmissionMember>().AsNoTracking() join s in db.Set<MeasurementSubmission>().AsNoTracking() on m.SubmissionId equals s.Id
            where m.ProjectId==id && ids.Contains(m.StructureId) && m.EvidenceVersion==1 && !s.IsLegacy && s.EvidenceStatus==IncrementalEvidence.Complete
            && db.Set<EngineeringReceipt>().Any(r=>r.ProjectId==id && r.SubmissionId==m.SubmissionId) orderby s.Number,m.Position select m).ToArrayAsync(token);
        if(rows.Length!=refs.Count || rows.Any(m=>!refs.Contains(new(m.StructureId,m.SubmissionId,m.EvidenceVersion)))) throw new DocumentException(409,"EngineeringSourceInvalid");
        var owned=await (from m in db.Set<EngineeringAssignedStructure>() join a in db.Set<WorkPackageAssignment>() on m.AssignmentId equals a.Id
            join p in db.Set<EngineeringPackage>() on a.PackageId equals p.Id where m.ProjectId==id && ids.Contains(m.StructureId) && a.EndedAt==null && p.Status=="Open" && (coordinator || a.Subject==actor.Subject) select m.StructureId).CountAsync(token);
        if(owned!=rows.Length) throw new DocumentException(403,"EngineeringAccessDenied");
        if(await db.Set<ActiveOpeningRecordMember>().AnyAsync(m=>m.ProjectId==id && ids.Contains(m.StructureId),token)) throw new DocumentException(409,"EngineeringStructureReserved");
        return rows;
    }
    private async Task<OpeningRecordMember[]> Members(Guid id,Guid acta,CancellationToken token)=>await db.Set<OpeningRecordMember>().AsNoTracking().Where(m=>m.ProjectId==id && m.OpeningRecordId==acta).OrderBy(m=>m.Position).ToArrayAsync(token);
    private async Task<OpeningDocumentScope> CoveredDocument(Guid id,Guid acta,Guid doc,OpeningRecordMember[] members,CancellationToken token)
    {
        var scope=await db.Set<OpeningDocumentScope>().AsNoTracking().SingleOrDefaultAsync(s=>s.ProjectId==id && s.OpeningRecordId==acta && s.DocumentId==doc && s.Kind=="ENGINEERING",token) ?? throw new DocumentException(409,"EngineeringActaRequired");
        var ids=await db.Set<OpeningDocumentMember>().Where(m=>m.ProjectId==id && m.OpeningRecordId==acta && m.DocumentId==doc).Select(m=>m.StructureId).ToArrayAsync(token);
        if(!ids.ToHashSet().SetEquals(members.Select(m=>m.StructureId))) throw new DocumentException(409,"EngineeringDocumentScopeConflict");
        return scope;
    }
    public async Task<EngineeringCommandResult> ExecuteAsync(Guid id,EngineeringCommand c,DecisionActor actor,CancellationToken token)
    {
        await using var tx=await db.Database.BeginTransactionAsync(token);
        var tasks=await LockProject(id,token); await RequireNative(id,actor,token);
        var fingerprint=Fingerprint(c); var old=await WorkspaceReplay(id,c.RequestKey,fingerprint,actor,token);
        if(old!=null) return new(old.TargetId);
        var at=timing.UtcNow; var added=new List<object>(); var touched=new List<object>(); var changedTasks=new List<ProjectTask>();
        Guid target; long revision;
        EfProjectNotificationWriter.NotificationWriteSet? staged=null;
        try
        {
            if(c.Action is "CreatePackage" or "ReassignPackage")
            {
                if(actor.Subject!=engineering.Subject || !engineering.Subjects.Contains(c.Subject!,StringComparer.Ordinal)) throw new DocumentException(403,"EngineeringAccessDenied");
                EngineeringPackage package; EngineeringAssignedStructure[] claims;
                if(c.Action=="CreatePackage")
                {
                    var workspace=await db.Set<EngineeringWorkspace>().AsNoTracking().SingleAsync(w=>w.ProjectId==id,token);
                    Revision(workspace.Revision,c.ExpectedRevision); db.Attach(workspace); touched.Add(workspace); workspace.Advance();
                    var selected=await Selected(id,c.Structures!,actor,true,token); var ids=selected.Select(m=>m.StructureId).ToArray();
                    claims=await db.Set<EngineeringAssignedStructure>().AsNoTracking().Where(m=>m.ProjectId==id && ids.Contains(m.StructureId)).ToArrayAsync(token);
                    var sourceAssignmentIds=claims.Select(m=>m.AssignmentId).ToArray();
                    var sourcePackages=await (from a in db.Set<WorkPackageAssignment>().AsNoTracking() join p in db.Set<EngineeringPackage>().AsNoTracking() on a.PackageId equals p.Id where sourceAssignmentIds.Contains(a.Id) select p).ToArrayAsync(token);
                    if(sourcePackages.Any(p=>!p.IsIntake)) throw new DocumentException(409,"EngineeringAssignmentConflict");
                    var remainingClaims=await db.Set<EngineeringAssignedStructure>().AsNoTracking().Where(m=>m.ProjectId==id && !ids.Contains(m.StructureId)).Select(m=>m.AssignmentId).ToArrayAsync(token);
                    foreach(var source in sourcePackages.DistinctBy(p=>p.Id).Where(p=>!remainingClaims.Contains(p.CurrentAssignmentId)))
                    {
                        db.Attach(source);touched.Add(source);source.Close(at,true);
                        var ended=await db.Set<WorkPackageAssignment>().AsNoTracking().SingleAsync(a=>a.Id==source.CurrentAssignmentId,token);db.Attach(ended);touched.Add(ended);ended.End(at);
                    }
                    var assignment=new WorkPackageAssignment(Guid.NewGuid(),id,Guid.NewGuid(),5,c.Subject!,at,null,null);
                    var task=new ProjectTask(id,OperationalArea.Engineering,ProjectTaskType.EngineeringPackage,c.Subject!,at);task.StartUntracked(at);
                    package=new(assignment,false,actor.Subject,task.Id); added.AddRange([assignment,task,package]);changedTasks.Add(task);
                }
                else
                {
                    package=await db.Set<EngineeringPackage>().AsNoTracking().SingleOrDefaultAsync(p=>p.ProjectId==id && p.Id==c.TargetId,token) ?? throw new DocumentException(404,"EngineeringPackageNotFound");
                    Revision(package.Revision,c.ExpectedRevision);
                    if(package.IsIntake || package.Status!="Open") throw new DocumentException(409,"EngineeringLocked");
                    claims=await db.Set<EngineeringAssignedStructure>().AsNoTracking().Where(m=>m.ProjectId==id && m.AssignmentId==package.CurrentAssignmentId).ToArrayAsync(token);
                    var ids=claims.Select(m=>m.StructureId).ToArray();
                    if(await db.Set<ActiveOpeningRecordMember>().AnyAsync(m=>m.ProjectId==id && ids.Contains(m.StructureId),token)) throw new DocumentException(409,"EngineeringStructureReserved");
                    var previous=await db.Set<WorkPackageAssignment>().AsNoTracking().SingleAsync(a=>a.Id==package.CurrentAssignmentId,token);
                    db.Attach(previous);touched.Add(previous);previous.End(at);
                    // Flush the ended assignment before inserting its successor under the partial unique index.
                    await db.SaveChangesAsync(token);
                    var oldTask=tasks.Single(t=>t.Id==package.TaskId);db.Attach(oldTask);touched.Add(oldTask);oldTask.Complete(at);changedTasks.Add(oldTask);
                    var task=new ProjectTask(id,OperationalArea.Engineering,ProjectTaskType.EngineeringPackage,c.Subject!,at);task.StartUntracked(at);
                    var assignment=new WorkPackageAssignment(Guid.NewGuid(),id,package.Id,5,c.Subject!,at,null,null);
                    db.Attach(package);touched.Add(package);package.Reassign(assignment,task.Id);added.AddRange([assignment,task]);changedTasks.Add(task);
                }
                foreach(var claim in claims)
                {
                    var member=new WorkPackageMember(package.CurrentAssignmentId,id,claim.SubmissionId,claim.StructureId,claim.EvidenceVersion);
                    added.Add(member); db.Attach(claim);touched.Add(claim);claim.Assign(member);
                }
                target=package.Id;revision=package.Revision;
            }
            else
            {
                EngineeringOpeningState state;
                if(c.Action=="CreateActa")
                {
                    var workspace=await db.Set<EngineeringWorkspace>().AsNoTracking().SingleAsync(w=>w.ProjectId==id,token);Revision(workspace.Revision,c.ExpectedRevision);
                    var selected=await Selected(id,c.Structures!,actor,false,token);
                    var number=(await db.Set<OpeningRecord>().Where(a=>a.ProjectId==id).MaxAsync(a=>(int?)a.Number,token) ?? 0)+1;
                    var acta=new OpeningRecord(Guid.NewGuid(),id,number,at,null,IncrementalEvidence.Complete);state=new(acta,actor.Subject);
                    added.AddRange([acta,state]);db.Attach(workspace);touched.Add(workspace);workspace.Advance();
                    added.AddRange(selected.Select((m,i)=>new OpeningRecordMember(acta.Id,id,m.StructureId,m.SubmissionId,m.EvidenceVersion,i)));
                    added.AddRange(selected.Select(m=>new ActiveOpeningRecordMember(acta.Id,id,m.StructureId)));
                }
                else
                {
                    state=await OwnedActa(id,c.TargetId!.Value,actor,token);
                    if(state.ConfirmedAt!=null) throw new DocumentException(409,"EngineeringLocked");Revision(state.Revision,c.ExpectedRevision);
                    db.Attach(state);touched.Add(state);var members=await Members(id,state.OpeningRecordId,token);
                    if(c.Action=="AddStructures")
                    {
                        var selected=await Selected(id,c.Structures!,actor,false,token);
                        // Existing immutable positions stay stable; display ordering uses the original source keys.
                        added.AddRange(selected.Select((m,i)=>new OpeningRecordMember(state.OpeningRecordId,id,m.StructureId,m.SubmissionId,m.EvidenceVersion,members.Length+i)));
                        added.AddRange(selected.Select(m=>new ActiveOpeningRecordMember(state.OpeningRecordId,id,m.StructureId)));state.Advance();
                    }
                    else if(c.Action=="SelectDocument")
                    { await CoveredDocument(id,state.OpeningRecordId,c.DocumentId!.Value,members,token);state.SetDocument(c.DocumentId.Value); }
                    else if(c.Action=="ConfirmActa")
                    {
                        if(members.Length==0 || state.CurrentDocumentId==null) throw new DocumentException(409,"EngineeringActaRequired");
                        var ids=members.Select(m=>m.StructureId).ToArray();
                        var checks=await db.Set<EngineeringEvidenceCheck>().AsNoTracking().Where(x=>x.ProjectId==id && ids.Contains(x.StructureId)).OrderBy(x=>x.StructureId).ToArrayAsync(token);
                        if(checks.Length!=members.Length || checks.Any(x=>!x.Completed || !members.Any(m=>m.StructureId==x.StructureId && m.SubmissionId==x.SubmissionId && m.EvidenceVersion==x.EvidenceVersion))) throw new DocumentException(409,"EngineeringIncomplete");
                        var scope=await CoveredDocument(id,state.OpeningRecordId,state.CurrentDocumentId.Value,members,token);
                        var doc=await db.ProjectDocuments.AsNoTracking().SingleAsync(d=>d.Id==scope.DocumentId && d.ProjectId==id && d.IsEngineeringDocument,token);await VerifiedBytes(doc,scope.Sha256,token);
                        state.Confirm(at,actor.Subject,JsonSerializer.Serialize(new{Members=members.Select(Ref),Checks=checks,scope.DocumentId,scope.Version,scope.Sha256,ConfirmedAt=at,ConfirmedBy=actor.Subject}));
                        var confirmed=await (from m in db.Set<ActiveOpeningRecordMember>() join a in db.Set<EngineeringOpeningState>() on m.OpeningRecordId equals a.OpeningRecordId where m.ProjectId==id && a.ConfirmedAt!=null select m.StructureId).ToArrayAsync(token);
                        var done=confirmed.Concat(ids).ToHashSet();
                        var packages=await db.Set<EngineeringPackage>().AsNoTracking().Where(p=>p.ProjectId==id && p.Status=="Open").ToArrayAsync(token);
                        var claims=await db.Set<EngineeringAssignedStructure>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
                        foreach(var p in packages)
                        {
                            var assigned=claims.Where(m=>m.AssignmentId==p.CurrentAssignmentId).ToArray();
                            if(assigned.Length==0 || !assigned.All(m=>done.Contains(m.StructureId))) continue;
                            db.Attach(p);touched.Add(p);p.Close(at);
                            var assignment=await db.Set<WorkPackageAssignment>().AsNoTracking().SingleAsync(a=>a.Id==p.CurrentAssignmentId,token);db.Attach(assignment);touched.Add(assignment);assignment.End(at);
                            if(p.TaskId is {} taskId) { var task=tasks.Single(t=>t.Id==taskId);db.Attach(task);touched.Add(task);task.Complete(at);changedTasks.Add(task); }
                        }
                    }
                    else throw new DocumentException(400,"InvalidEngineeringCommand");
                }
                target=state.OpeningRecordId;revision=state.Revision;
            }
            added.Add(new EngineeringWorkspaceEvent(id,c.RequestKey,fingerprint,c.Action,target,revision,at,actor.Subject,JsonSerializer.Serialize(c)));
            db.AddRange(added);if(changedTasks.Count>0) staged=await notifications.StageAsync(changedTasks,at,token);
            await db.SaveChangesAsync(token);await tx.CommitAsync(token);return new(target);
        }
        catch { staged?.RestoreTracking();throw; }
        finally { foreach(var e in added.Concat(touched)) db.Entry(e).State=EntityState.Detached; }
    }
}
