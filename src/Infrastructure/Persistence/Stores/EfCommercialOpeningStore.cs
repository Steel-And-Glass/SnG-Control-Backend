using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfCommercialOpeningStore(SnGControlDbContext db,TaskTiming timing,EfProjectNotificationWriter notifications,IStreamingFileStorage files,IEngineeringAssigneeIdentity engineering,IRoleResolver roles):ICommercialOpeningStore
{
    private static string Json(object value)=>JsonSerializer.Serialize(value);
    private static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value));
    private static string Fingerprint(object value)=>Hash(Encoding.UTF8.GetBytes(Json(value)));
    private static DocumentException Conflict(string code)=>new(409,code);
    private static void Revision(long actual,long expected){if(actual!=expected)throw Conflict("OpeningRevisionConflict");}
    private Task<bool> CanRead(Guid id,ProjectReadScope scope,CancellationToken token)=>db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(p=>p.Id==id,token);
    private static void Advisor(string? subject,IRoleResolver? roles){if(string.IsNullOrWhiteSpace(subject)||roles?.Resolve(subject).Contains(SystemRole.CommercialAdvisor)!=true)throw new DocumentException(503,"OpeningAdvisorNotConfigured");}
    private void Engineer(string subject){if(!engineering.Subjects.Contains(subject,StringComparer.Ordinal)||!roles.Resolve(subject).Contains(SystemRole.Engineering))throw new DocumentException(503,"EngineeringAssigneeNotConfigured");}
    private static ProjectTask NewTask(Guid id,ProjectTaskType type,string subject,DateTime at){var task=new ProjectTask(id,type==ProjectTaskType.PresentOpeningRecord?OperationalArea.Commercial:OperationalArea.Engineering,type,subject,at);task.StartUntracked(at);return task;}
    internal static async Task<(ProjectTask Task,IReadOnlyList<object> Entities)> CreateInitialAsync(SnGControlDbContext db,EngineeringOpeningState state,IRoleResolver? roles,DateTime at,CancellationToken token)
    {
        if(state.ConfirmedAt==null||state.CurrentDocumentId==null)throw Conflict("OpeningSourceInvalid");
        var subject=await db.Projects.Where(p=>p.Id==state.ProjectId).Select(p=>p.CreatedByGoogleSubject).SingleAsync(token);Advisor(subject,roles);
        if(await db.Set<CommercialOpeningRound>().AnyAsync(r=>r.OpeningRecordId==state.OpeningRecordId,token))throw Conflict("OpeningAlreadyInitialized");
        var members=await db.Set<OpeningRecordMember>().AsNoTracking().Where(m=>m.ProjectId==state.ProjectId&&m.OpeningRecordId==state.OpeningRecordId).ToArrayAsync(token);
        if(members.Length==0)throw Conflict("OpeningSourceInvalid");
        var task=NewTask(state.ProjectId,ProjectTaskType.PresentOpeningRecord,subject!,at);
        var round=new CommercialOpeningRound(Guid.NewGuid(),state.ProjectId,state.OpeningRecordId,1,task,state.CurrentDocumentId.Value,at);
        var entities=new List<object>{task,new OpeningRoundLink(round.Id,state.ProjectId,state.OpeningRecordId,1),round};entities.AddRange(members.Select(m=>new CommercialOpeningRow(round,m)));return(task,entities);
    }
    private async Task<(Project Project,ProjectTask[] Tasks,EngineeringOpeningState Acta)> Lock(Guid id,Guid acta,CancellationToken token)
    {
        var project=await db.Projects.FromSqlInterpolated($"""SELECT * FROM "Projects" WHERE "Id"={id} FOR UPDATE""").AsNoTracking().SingleOrDefaultAsync(token)??throw new DocumentException(404,"ProjectNotFound");
        var tasks=await db.ProjectTasks.FromSqlInterpolated($"""SELECT * FROM "ProjectTasks" WHERE "ProjectId"={id} FOR UPDATE""").AsNoTracking().ToArrayAsync(token);
        if(!await db.MeasurementChecklists.AnyAsync(c=>c.ProjectId==id&&c.IsIncremental,token))throw Conflict("OpeningSourceInvalid");
        var state=await db.Set<EngineeringOpeningState>().AsNoTracking().SingleOrDefaultAsync(a=>a.ProjectId==id&&a.OpeningRecordId==acta&&a.ConfirmedAt!=null,token)??throw new DocumentException(404,"OpeningActaNotFound");
        return(project,tasks,state);
    }
    private async Task<CommercialOpeningEvent?> Replay(Guid id,Guid key,string fp,DecisionActor actor,CancellationToken token)
    {var old=await db.Set<CommercialOpeningEvent>().AsNoTracking().SingleOrDefaultAsync(e=>e.ProjectId==id&&e.RequestKey==key,token);if(old!=null&&(old.Fingerprint!=fp||old.ActorSubject!=actor.Subject))throw Conflict("OpeningRequestConflict");return old;}
    private void Authorize(Project project,CommercialOpeningRound? round,CommercialOpeningReview? review,ProjectTask[] tasks,DecisionActor actor,SystemRole role,bool reviewing)
    {
        if(reviewing){if(role!=SystemRole.Engineering||review?.Subject!=actor.Subject||!engineering.Subjects.Contains(actor.Subject,StringComparer.Ordinal)||!roles.Resolve(actor.Subject).Contains(role)||!tasks.Any(t=>t.Id==review.TaskId&&t.AssignedToGoogleSubject==actor.Subject))throw new DocumentException(403,"OpeningAccessDenied");}
        else if(role!=SystemRole.CommercialAdvisor||project.CreatedByGoogleSubject!=actor.Subject||!roles.Resolve(actor.Subject).Contains(role)||(round!=null&&(round.AdvisorSubject!=actor.Subject||!tasks.Any(t=>t.Id==round.TaskId&&t.AssignedToGoogleSubject==actor.Subject))))throw new DocumentException(403,"OpeningAccessDenied");
    }
    private static CommercialOpeningRow[] Select(CommercialOpeningRow[] rows,IReadOnlyList<CommercialOpeningSelection> selection,bool revision=true)
    {
        var chosen=selection.Select(s=>rows.SingleOrDefault(r=>r.StructureId==s.StructureId)??throw Conflict("OpeningStructureNotInActa")).ToArray();
        if(chosen.Length==0||chosen.Length>500||chosen.Select(r=>r.StructureId).Distinct().Count()!=chosen.Length)throw new DocumentException(400,"InvalidOpeningCommand");
        foreach(var row in chosen){var wanted=selection.Single(s=>s.StructureId==row.StructureId);if(wanted.EvidenceVersion!=row.EvidenceVersion)throw Conflict("OpeningEvidenceConflict");if(revision)Revision(row.Revision,wanted.ExpectedRevision);}
        return chosen;
    }
    public async Task<CommercialOpeningResult> CommandAsync(Guid id,Guid actaId,CommercialOpeningCommand c,DecisionActor actor,SystemRole role,CancellationToken token)
    {
        await using var tx=await db.Database.BeginTransactionAsync(token);var(project,tasks,acta)=await Lock(id,actaId,token);
        var round=c.RoundId is {} rid?await db.Set<CommercialOpeningRound>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==rid&&r.OpeningRecordId==actaId&&r.ProjectId==id,token):null;
        var review=c.ReviewId is {} vid?await db.Set<CommercialOpeningReview>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==vid&&r.RoundId==c.RoundId&&r.ProjectId==id&&r.OpeningRecordId==actaId,token):null;
        var reviewing=c.Action is "DeclareChanged" or "ReviewCheck" or "CloseReview";Authorize(project,round,review,tasks,actor,role,reviewing);
        var fp=Fingerprint(new{actaId,c});var replay=await Replay(id,c.RequestKey,fp,actor,token);if(replay!=null)return new(replay.ResultId);
        var added=new List<object>();var touched=new List<object>();var changedTasks=new List<ProjectTask>();EfProjectNotificationWriter.NotificationWriteSet? staged=null;var at=timing.UtcNow;Guid result;
        void Touch(object o){if(touched.Contains(o))return;db.Attach(o);touched.Add(o);}
        try
        {
            if(c.Action=="Receive")
            {
                round=await db.Set<CommercialOpeningRound>().AsNoTracking().Where(r=>r.OpeningRecordId==actaId).OrderBy(r=>r.Number).FirstOrDefaultAsync(token);
                if(round==null){var created=await CreateInitialAsync(db,acta,roles,at,token);added.AddRange(created.Entities);changedTasks.Add(created.Task);round=created.Entities.OfType<CommercialOpeningRound>().Single();}
                result=round.Id;
            }
            else
            {
                if(round==null)throw new DocumentException(404,"OpeningRoundNotFound");
                if(round.SupersededAt!=null||round.ResolvedAt!=null||tasks.Single(t=>t.Id==round.TaskId).Status!=ProjectTaskStatus.InProgress)throw Conflict("OpeningRoundStateConflict");
                var rows=await db.Set<CommercialOpeningRow>().AsNoTracking().Where(r=>r.RoundId==round.Id).OrderBy(r=>r.StructureId).ToArrayAsync(token);
                var task=tasks.Single(t=>t.Id==round.TaskId);result=round.Id;Touch(round);
                if(c.Action=="Present"){Revision(round.Revision,c.ExpectedRevision);await VerifyDocument(id,actaId,round.DocumentId,token);round.Present(at);}
                else if(c.Action is "RequestChanges" or "Accept")
                {
                    if(round.PresentedAt==null)throw Conflict("OpeningRoundStateConflict");
                    var selected=Select(rows,c.Structures!);foreach(var r in selected)Touch(r);
                    if(c.Action=="RequestChanges")
                    {
                        Engineer(acta.ResponsibleSubject);
                        review=await db.Set<CommercialOpeningReview>().AsNoTracking().SingleOrDefaultAsync(r=>r.OpeningRecordId==actaId&&r.CompletedAt==null,token);
                        if(review==null){var rt=NewTask(id,ProjectTaskType.ReviewOpeningRecord,acta.ResponsibleSubject,at);review=new(round,rt,at);added.AddRange([rt,review]);changedTasks.Add(rt);}else Touch(review);
                        if(review.RoundId!=round.Id)throw Conflict("OpeningRoundStateConflict");
                        var members=await db.Set<CommercialOpeningReviewMember>().AsNoTracking().Where(m=>m.ReviewId==review.Id).ToArrayAsync(token);
                        foreach(var r in selected){r.Review();var m=members.SingleOrDefault(m=>m.StructureId==r.StructureId);if(m==null){m=new(review,r,true);added.Add(m);}else{Touch(m);m.Request(at,actor.Subject);}}
                        review.Invalidate();result=review.Id;
                    }
                    else
                    {
                        if(c.Attest!=true||c.DocumentId==null)throw Conflict("OpeningSignedActaRequired");Engineer(acta.ResponsibleSubject);
                        var doc=await db.Set<CommercialOpeningDocument>().AsNoTracking().SingleOrDefaultAsync(d=>d.DocumentId==c.DocumentId&&d.ProjectId==id&&d.OpeningRecordId==actaId&&d.RoundId==round.Id&&d.Kind=="SIGNED",token)??throw Conflict("OpeningSignedActaRequired");
                        var scope=await db.Set<CommercialOpeningDocumentMember>().AsNoTracking().Where(m=>m.DocumentId==doc.DocumentId).ToArrayAsync(token);
                        foreach(var r in selected)if(r.Approval!="AwaitingApproval"||!scope.Any(m=>m.StructureId==r.StructureId&&m.EvidenceVersion==r.EvidenceVersion&&m.EvidenceDocumentId==r.EvidenceDocumentId))throw Conflict("OpeningEvidenceConflict");
                        var ids=selected.Select(r=>r.StructureId).ToArray();if(await db.Set<OpeningAcceptanceMember>().AnyAsync(m=>m.ProjectId==id&&m.OpeningRecordId==actaId&&ids.Contains(m.StructureId),token))throw Conflict("OpeningAlreadyReleased");
                        await VerifyDocument(id,actaId,doc.DocumentId,token);await VerifyDocument(id,actaId,round.DocumentId,token);
                        var acceptance=new OpeningAcceptance(Guid.NewGuid(),id,actaId,round.Id,round.DocumentId,doc.DocumentId,at,actor.Subject,false,c.RequestKey,fp);
                        var filing=ProjectTask.ForOpeningAcceptance(acceptance,acta.ResponsibleSubject,at);added.AddRange([acceptance,filing,new OpeningFilingDelivery(Guid.NewGuid(),id,acceptance.Id,filing.Id)]);changedTasks.Add(filing);
                        foreach(var r in selected){r.Accept(acceptance.Id);added.Add(new OpeningAcceptanceMember(acceptance.Id,id,actaId,r.StructureId));added.Add(new CommercialOpeningAcceptanceEvidence(acceptance,scope.Single(m=>m.StructureId==r.StructureId)));}result=acceptance.Id;
                    }
                    round.Respond(at);
                    if(rows.All(r=>r.Approval=="Confirmed")){
                        // Persist the final accepted rows before freezing the round, in this same transaction.
                        db.AddRange(added);await db.SaveChangesAsync(token);round.Resolve(at);Touch(task);task.Complete(at);changedTasks.Add(task);}
                }
                else if(reviewing)
                {
                    if(review==null||review.CompletedAt!=null||tasks.Single(t=>t.Id==review.TaskId).Status!=ProjectTaskStatus.InProgress)throw Conflict("OpeningReviewClosed");
                    var members=(await db.Set<CommercialOpeningReviewMember>().AsNoTracking().Where(m=>m.ReviewId==review.Id).ToArrayAsync(token)).ToList();Touch(review);
                    if(c.Action=="ReviewCheck")
                    {
                        var selected=c.Structures!.Single();var m=members.SingleOrDefault(m=>m.StructureId==selected.StructureId)??throw Conflict("OpeningStructureNotInReview");
                        Revision(m.Revision,selected.ExpectedRevision);if(m.EvidenceVersion!=selected.EvidenceVersion)throw Conflict("OpeningEvidenceConflict");Touch(m);m.Check(c.Completed!.Value,at,actor.Subject);review.Advance();
                    }
                    else if(c.Action=="DeclareChanged")
                    {
                        Revision(review.Revision,c.ExpectedRevision);var selected=c.Structures!.Count==0?[]:Select(rows,c.Structures!);
                        foreach(var r in selected){if(r.AcceptanceId!=null)throw Conflict("OpeningReleasedEvidenceLocked");if(!members.Any(m=>m.StructureId==r.StructureId)){Touch(r);r.Review();var m=new CommercialOpeningReviewMember(review,r,false);members.Add(m);added.Add(m);}}
                        foreach(var m in members){if(!added.Contains(m))Touch(m);m.Declare(selected.Any(r=>r.StructureId==m.StructureId));}review.Declare();
                    }
                    else
                    {
                        Revision(review.Revision,c.ExpectedRevision);Advisor(project.CreatedByGoogleSubject,roles);
                        if(!review.Declared||members.Count==0||members.Any(m=>!m.Completed)||review.DocumentId==null||c.DocumentId!=review.DocumentId)throw Conflict("OpeningReviewIncomplete");
                        var doc=await db.Set<CommercialOpeningDocument>().AsNoTracking().SingleAsync(d=>d.DocumentId==review.DocumentId&&d.ReviewId==review.Id&&d.Kind=="REVISED",token);
                        var scope=await db.Set<CommercialOpeningDocumentMember>().AsNoTracking().Where(m=>m.DocumentId==doc.DocumentId).ToArrayAsync(token);
                        if(scope.Length!=rows.Length||rows.Any(r=>!scope.Any(m=>m.StructureId==r.StructureId&&m.EvidenceVersion==r.EvidenceVersion+(members.Any(x=>x.StructureId==r.StructureId&&x.Changed)?1:0))))throw Conflict("OpeningAffectedDeclarationChanged");
                        await VerifyDocument(id,actaId,doc.DocumentId,token);
                        review.Complete(at);round.Supersede(at);Touch(task);task.Complete(at);changedTasks.Add(task);
                        var rt=tasks.Single(t=>t.Id==review.TaskId);Touch(rt);rt.Complete(at);changedTasks.Add(rt);
                        // Free unique current-round/review slots before adding their successors, within this transaction.
                        await db.SaveChangesAsync(token);
                        var nt=NewTask(id,ProjectTaskType.PresentOpeningRecord,project.CreatedByGoogleSubject!,at);var next=new CommercialOpeningRound(Guid.NewGuid(),id,actaId,checked(round.Number+1),nt,doc.DocumentId,at);
                        added.AddRange([nt,new OpeningRoundLink(next.Id,id,actaId,next.Number),next]);changedTasks.Add(nt);
                        var manifest=await db.Set<OpeningRecordMember>().AsNoTracking().Where(m=>m.ProjectId==id&&m.OpeningRecordId==actaId).ToArrayAsync(token);
                        foreach(var m in manifest){var previous=rows.Single(r=>r.StructureId==m.StructureId);var checkedMember=members.SingleOrDefault(r=>r.StructureId==m.StructureId);added.Add(new CommercialOpeningRow(next,m,previous,checkedMember?.Changed==true,checkedMember!=null));}result=next.Id;
                    }
                    if(c.Action!="CloseReview")round.Advance(at);
                }
                else throw new DocumentException(400,"InvalidOpeningCommand");
            }
            added.Add(new CommercialOpeningEvent(id,actaId,round!.Id,c.RequestKey,fp,c.Action,result,at,actor.Subject,Json(c)));
            db.AddRange(added.Where(e=>db.Entry(e).State==EntityState.Detached));if(changedTasks.Count>0)staged=await notifications.StageAsync(changedTasks,at,token);await db.SaveChangesAsync(token);await tx.CommitAsync(token);return new(result);
        }
        catch(InvalidOperationException e)when(e.Message.StartsWith("Opening",StringComparison.Ordinal)){staged?.RestoreTracking();throw Conflict(e.Message);}
        catch{staged?.RestoreTracking();throw;}
        finally{foreach(var e in added.Concat(touched))db.Entry(e).State=EntityState.Detached;}
    }
}
