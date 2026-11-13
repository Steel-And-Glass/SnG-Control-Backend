using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfEngineeringStore
{
    private async Task<EngineeringWorkspaceView> WorkspaceView(Guid id,ProjectReadScope scope,CancellationToken token)
    {
        var project=await db.Projects.AsNoTracking().SingleAsync(p=>p.Id==id,token);
        var workspace=await db.Set<EngineeringWorkspace>().AsNoTracking().SingleAsync(w=>w.ProjectId==id,token);
        var packages=await db.Set<EngineeringPackage>().AsNoTracking().Where(p=>p.ProjectId==id).OrderBy(p=>p.CreatedAt).ThenBy(p=>p.Id).ToArrayAsync(token);
        var assignments=await db.Set<WorkPackageAssignment>().AsNoTracking().Where(a=>a.ProjectId==id && a.Area==5).OrderBy(a=>a.AssignedAt).ThenBy(a=>a.Id).ToArrayAsync(token);
        var assignmentMembers=await db.Set<WorkPackageMember>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var claims=await db.Set<EngineeringAssignedStructure>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var roots=await db.Set<OpeningRecord>().AsNoTracking().Where(a=>a.ProjectId==id && a.LegacyChecklistId==null).OrderBy(a=>a.Number).ToArrayAsync(token);
        var states=await db.Set<EngineeringOpeningState>().AsNoTracking().Where(a=>a.ProjectId==id).ToArrayAsync(token);
        var members=await db.Set<OpeningRecordMember>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var sourceOrder=await (from m in db.Set<MeasurementSubmissionMember>() join s in db.Set<MeasurementSubmission>() on m.SubmissionId equals s.Id where m.ProjectId==id && m.EvidenceVersion==1 orderby s.Number,m.Position select m.StructureId).ToArrayAsync(token);
        var order=sourceOrder.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
        EngineeringReference[] Ordered(IEnumerable<EngineeringReference> refs)=>refs.OrderBy(r=>order.GetValueOrDefault(r.StructureId,int.MaxValue)).ToArray();
        var checks=await db.Set<EngineeringEvidenceCheck>().AsNoTracking().Where(c=>c.ProjectId==id).ToArrayAsync(token);
        var docs=await (from s in db.Set<OpeningDocumentScope>().AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on s.DocumentId equals d.Id where s.ProjectId==id && s.Kind=="ENGINEERING" orderby s.Version select new{s,d}).ToArrayAsync(token);
        var docMembers=await db.Set<OpeningDocumentMember>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var history=await db.Set<EngineeringWorkspaceEvent>().AsNoTracking().Where(e=>e.ProjectId==id).OrderBy(e=>e.At).ThenBy(e=>e.RequestKey).ToArrayAsync(token);
        var isTeam=scope.Role==SystemRole.Engineering && engineering.Subjects.Contains(scope.Subject,StringComparer.Ordinal);
        var manage=isTeam && scope.Subject==engineering.Subject;
        return new(project.Name,project.SgCode,workspace.Revision,manage,manage?engineering.Subjects:[],
            packages.Select(p=>{
                var a=assignments.Single(a=>a.Id==p.CurrentAssignmentId);
                return new EngineeringPackageView(p.Id,p.IsIntake,p.Status,p.Revision,a.Subject,p.CreatedAt,a.AssignedAt,p.CompletedAt,
                    Ordered(claims.Where(m=>m.AssignmentId==a.Id).Select(m=>new EngineeringReference(m.StructureId,m.SubmissionId,m.EvidenceVersion))),
                    assignments.Where(h=>h.PackageId==p.Id).Select(h=>new EngineeringAssignmentView(h.Id,h.Subject,h.AssignedAt,h.EndedAt,Ordered(assignmentMembers.Where(m=>m.AssignmentId==h.Id).Select(m=>new EngineeringReference(m.StructureId,m.SubmissionId,m.EvidenceVersion))))).ToArray());
            }).ToArray(),
            roots.Select(a=>{
                var s=states.Single(s=>s.OpeningRecordId==a.Id);var mm=members.Where(m=>m.OpeningRecordId==a.Id).ToArray();var ids=mm.Select(m=>m.StructureId).ToHashSet();
                var documents=docs.Where(d=>d.s.OpeningRecordId==a.Id).Select(d=>{
                    var covered=docMembers.Where(m=>m.OpeningRecordId==a.Id && m.DocumentId==d.d.Id).Select(m=>m.StructureId).ToHashSet();
                    return new EngineeringScopedDocument(new(DocumentResponse.From(d.d,new HashSet<Guid>()),d.s.Version,d.s.Sha256),Ordered(mm.Where(m=>covered.Contains(m.StructureId)).Select(Ref)),covered.SetEquals(ids));
                }).ToArray();
                var complete=ids.Count>0 && ids.All(i=>checks.Any(c=>c.StructureId==i && c.Completed));
                var ready=complete && documents.Any(d=>d.File.Document.Id==s.CurrentDocumentId && d.MatchesManifest);
                var status=s.ConfirmedAt!=null?"Confirmed":ready?"Ready":ids.Any(i=>checks.Any(c=>c.StructureId==i && c.Completed))?"InProgress":"Draft";
                var canModify=isTeam && s.ResponsibleSubject==scope.Subject && s.ConfirmedAt==null;
                return new EngineeringActaView(a.Id,a.Number,status,s.ResponsibleSubject,s.CreatedBySubject,a.CreatedAt,s.Revision,canModify,Ordered(mm.Select(Ref)),ScopeHash(mm),s.CurrentDocumentId,documents,s.ConfirmedAt,s.ConfirmedBySubject);
            }).ToArray(),history.Select(e=>new EngineeringWorkspaceHistory(e.Action,e.TargetId,e.Revision,e.At,e.ActorSubject)).ToArray());
    }
}
