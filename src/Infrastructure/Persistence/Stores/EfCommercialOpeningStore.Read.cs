using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfCommercialOpeningStore
{
    public async Task<CommercialOpeningState?> ReadAsync(Guid id,ProjectReadScope scope,CancellationToken token)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);
        if(!await CanRead(id,scope,token))return null;
        if(!await db.MeasurementChecklists.AnyAsync(c=>c.ProjectId==id&&c.IsIncremental,token))return null;
        var project=await db.Projects.AsNoTracking().SingleAsync(p=>p.Id==id,token);
        var actas=await (from a in db.Set<OpeningRecord>().AsNoTracking() join s in db.Set<EngineeringOpeningState>().AsNoTracking() on a.Id equals s.OpeningRecordId where a.ProjectId==id&&s.ConfirmedAt!=null orderby a.Number select new{a,s}).ToArrayAsync(token);
        var source=await (from m in db.Set<OpeningRecordMember>().AsNoTracking() join s in db.Set<MeasurementSubmissionMember>().AsNoTracking() on new{m.SubmissionId,m.StructureId,m.EvidenceVersion} equals new{s.SubmissionId,s.StructureId,s.EvidenceVersion} join submission in db.Set<MeasurementSubmission>() on s.SubmissionId equals submission.Id where m.ProjectId==id orderby submission.Number,s.Position select new{m,s,submission.Number}).ToArrayAsync(token);
        var rounds=await db.Set<CommercialOpeningRound>().AsNoTracking().Where(r=>r.ProjectId==id).OrderBy(r=>r.Number).ToArrayAsync(token);
        var rows=await db.Set<CommercialOpeningRow>().AsNoTracking().Where(r=>r.ProjectId==id).ToArrayAsync(token);
        var reviews=await db.Set<CommercialOpeningReview>().AsNoTracking().Where(r=>r.ProjectId==id).OrderBy(r=>r.CreatedAt).ToArrayAsync(token);
        var reviewMembers=await db.Set<CommercialOpeningReviewMember>().AsNoTracking().Where(r=>r.ProjectId==id).ToArrayAsync(token);
        var docs=await (from d in db.Set<OpeningDocumentScope>().AsNoTracking() join p in db.ProjectDocuments.AsNoTracking() on d.DocumentId equals p.Id where d.ProjectId==id orderby p.UploadedAt,d.Version select new{d,p}).ToArrayAsync(token);
        var docLinks=await db.Set<CommercialOpeningDocument>().AsNoTracking().Where(d=>d.ProjectId==id).ToArrayAsync(token);
        var docMembers=await db.Set<CommercialOpeningDocumentMember>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var initialMembers=await db.Set<OpeningDocumentMember>().AsNoTracking().Where(m=>m.ProjectId==id).ToArrayAsync(token);
        var acceptances=await db.Set<OpeningAcceptance>().AsNoTracking().Where(a=>a.ProjectId==id&&!a.IsLegacy).OrderBy(a=>a.At).ToArrayAsync(token);
        var accepted=await db.Set<CommercialOpeningAcceptanceEvidence>().AsNoTracking().Where(a=>a.ProjectId==id).ToArrayAsync(token);
        var filings=await db.Set<OpeningFilingDelivery>().AsNoTracking().Where(f=>f.ProjectId==id).ToArrayAsync(token);
        var events=await db.Set<CommercialOpeningEvent>().AsNoTracking().Where(e=>e.ProjectId==id).OrderBy(e=>e.At).ThenBy(e=>e.RequestKey).ToArrayAsync(token);
        var tasks=await db.ProjectTasks.AsNoTracking().Where(t=>t.ProjectId==id).ToDictionaryAsync(t=>t.Id,token);var now=timing.UtcNow;
        bool AdvisorAssigned(CommercialOpeningRound r)=>scope.Role==SystemRole.CommercialAdvisor&&scope.Subject==project.CreatedByGoogleSubject&&r.AdvisorSubject==scope.Subject&&tasks[r.TaskId].AssignedToGoogleSubject==scope.Subject&&tasks[r.TaskId].Status==ProjectTaskStatus.InProgress&&roles.Resolve(scope.Subject).Contains(SystemRole.CommercialAdvisor);
        decimal? Seconds(DateTime? start,DateTime? end)=>start==null||end==null?null:Math.Max(0,(end.Value-start.Value).Ticks/(decimal)TimeSpan.TicksPerSecond);
        var result=actas.Select(a=>new CommercialOpeningActaView(a.a.Id,a.a.Number,a.s.ResponsibleSubject,a.s.ConfirmedAt!.Value,rounds.Where(r=>r.OpeningRecordId==a.a.Id).Select(r=>r.LastActivityAt).DefaultIfEmpty(a.s.ConfirmedAt.Value).Max(),
            !rounds.Any(r=>r.OpeningRecordId==a.a.Id)&&scope.Role==SystemRole.CommercialAdvisor&&scope.Subject==project.CreatedByGoogleSubject&&roles.Resolve(scope.Subject).Contains(SystemRole.CommercialAdvisor),
            rounds.Where(r=>r.OpeningRecordId==a.a.Id).Select(r=>new CommercialOpeningRoundView(r.Id,r.Number,r.SupersededAt!=null?"Superseded":r.ResolvedAt!=null?"Resolved":r.PresentedAt==null?"Ready":"Presented",r.Revision,r.TaskId,r.DocumentId,r.AdvisorSubject,r.ReceivedAt,r.PresentedAt,r.FirstResponseAt,r.ResolvedAt,r.SupersededAt,
                Seconds(r.PresentedAt,r.ResolvedAt??r.SupersededAt??now),Seconds(r.PresentedAt,r.FirstResponseAt),Seconds(r.PresentedAt,r.ResolvedAt),r.PresentedAt==null&&r.SupersededAt==null&&AdvisorAssigned(r),r.PresentedAt!=null&&r.ResolvedAt==null&&r.SupersededAt==null&&AdvisorAssigned(r),
                source.Where(s=>s.m.OpeningRecordId==a.a.Id).Select(s=>{var row=rows.Single(x=>x.RoundId==r.Id&&x.StructureId==s.m.StructureId);return new CommercialOpeningRowView(JsonSerializer.Deserialize<MeasurementStructureView>(s.s.SnapshotCanonical)!,s.m.SubmissionId,s.Number,s.m.EvidenceVersion,row.EvidenceVersion,row.EvidenceDocumentId,row.Approval,row.AcceptanceId,row.Revision);}).ToArray())).ToArray(),
            reviews.Where(r=>r.OpeningRecordId==a.a.Id).Select(r=>new CommercialOpeningReviewView(r.Id,r.RoundId,r.TaskId,r.Subject,r.CreatedAt,r.CompletedAt,r.Revision,r.Declared,r.DocumentId,
                scope.Role==SystemRole.Engineering&&r.Subject==scope.Subject&&r.CompletedAt==null&&engineering.Subjects.Contains(scope.Subject,StringComparer.Ordinal)&&roles.Resolve(scope.Subject).Contains(SystemRole.Engineering)&&tasks[r.TaskId].AssignedToGoogleSubject==scope.Subject&&tasks[r.TaskId].Status==ProjectTaskStatus.InProgress,
                reviewMembers.Where(m=>m.ReviewId==r.Id).Select(m=>new CommercialOpeningReviewRow(m.StructureId,m.EvidenceVersion,m.Requested,m.Changed,m.Completed,m.Revision,m.ChangedAt,m.ChangedBySubject)).ToArray())).ToArray(),
            docs.Where(d=>d.d.OpeningRecordId==a.a.Id).Select(d=>{var link=docLinks.SingleOrDefault(l=>l.DocumentId==d.p.Id);return new CommercialOpeningDocumentView(link?.RoundId,link?.ReviewId,d.d.Kind,d.d.Version,d.d.Sha256,DocumentResponse.From(d.p,new HashSet<Guid>()),link==null?initialMembers.Where(m=>m.OpeningRecordId==a.a.Id&&m.DocumentId==d.p.Id).Select(m=>new CommercialOpeningDocumentRow(m.StructureId,1,d.p.Id)).ToArray():docMembers.Where(m=>m.DocumentId==d.p.Id).Select(m=>new CommercialOpeningDocumentRow(m.StructureId,m.EvidenceVersion,m.EvidenceDocumentId)).ToArray());}).ToArray(),
            acceptances.Where(x=>x.OpeningRecordId==a.a.Id).Select(x=>new CommercialOpeningAcceptanceView(x.Id,x.RoundId,x.SignedDocumentId,x.PresentedDocumentId,filings.Single(f=>f.AcceptanceId==x.Id).TaskId!.Value,x.At,x.ActorSubject,true,accepted.Where(m=>m.AcceptanceId==x.Id).Select(m=>new CommercialOpeningDocumentRow(m.StructureId,m.EvidenceVersion,m.EvidenceDocumentId)).ToArray())).ToArray(),
            events.Where(e=>e.OpeningRecordId==a.a.Id).Select(e=>{using var payload=JsonDocument.Parse(e.Canonical);var changes=Array.Empty<OpeningChange>();string? observations=null;if(e.Action=="RequestChanges"){var command=JsonSerializer.Deserialize<CommercialOpeningCommand>(e.Canonical)!;changes=command.Structures!.Select(s=>new OpeningChange(s.StructureId,s.Description!)).ToArray();observations=command.Observations;}return new CommercialOpeningEventView(e.RoundId,e.Action,e.At,e.ActorSubject,changes,observations);}).ToArray())).ToArray();
        await tx.CommitAsync(token);return new(id,now,result);
    }
}
