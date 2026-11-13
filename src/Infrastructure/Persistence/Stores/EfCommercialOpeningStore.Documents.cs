using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfCommercialOpeningStore
{
    private async Task<(ProjectDocument Document,byte[] Bytes)> VerifyDocument(Guid id,Guid acta,Guid document,CancellationToken token)
    {
        var scope=await db.Set<OpeningDocumentScope>().AsNoTracking().SingleOrDefaultAsync(d=>d.ProjectId==id&&d.OpeningRecordId==acta&&d.DocumentId==document,token)??throw new DocumentException(404,"DocumentNotFound");
        var doc=await db.ProjectDocuments.AsNoTracking().SingleAsync(d=>d.ProjectId==id&&d.Id==document&&d.IsEngineeringDocument,token);
        try{await using var stream=await files.OpenReadAsync(doc.StorageKey,token);var bytes=await EfEngineeringStore.ReadBounded(stream,token);if(bytes.LongLength!=doc.SizeBytes||Hash(bytes)!=scope.Sha256)throw Conflict("EngineeringActaIntegrity");return(doc,bytes);}
        catch(FileNotFoundException){throw Conflict("EngineeringActaUnavailable");}catch(DirectoryNotFoundException){throw Conflict("EngineeringActaUnavailable");}
    }
    public async Task<DocumentDownload> DownloadAsync(Guid id,Guid acta,Guid doc,ProjectReadScope scope,CancellationToken token)
    {if(!await CanRead(id,scope,token))throw new DocumentException(403,"OpeningAccessDenied");var result=await VerifyDocument(id,acta,doc,token);return new(new MemoryStream(result.Bytes,false),result.Document.OriginalFileName);}
    public async Task UploadAsync(Guid id,Guid actaId,Guid roundId,CommercialOpeningUpload c,IAsyncEnumerable<DocumentUpload> uploads,DecisionActor actor,SystemRole role,CancellationToken token)
    {
        if(!await CanRead(id,new(actor.Subject,role),token))throw new DocumentException(403,"OpeningAccessDenied");
        var reviewing=c.Kind=="REVISED";
        var accessProject=await db.Projects.AsNoTracking().SingleAsync(p=>p.Id==id,token);
        var accessRound=await db.Set<CommercialOpeningRound>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==roundId&&r.ProjectId==id&&r.OpeningRecordId==actaId,token)??throw new DocumentException(404,"OpeningRoundNotFound");
        var accessReview=c.ReviewId==null?null:await db.Set<CommercialOpeningReview>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==c.ReviewId&&r.RoundId==roundId&&r.OpeningRecordId==actaId&&r.ProjectId==id,token);
        var accessTasks=await db.ProjectTasks.AsNoTracking().Where(t=>t.ProjectId==id).ToArrayAsync(token);Authorize(accessProject,accessRound,accessReview,accessTasks,actor,role,reviewing);
        byte[]? bytes=null;string? name=null;
        await foreach(var upload in uploads.WithCancellation(token)){if(bytes!=null||upload.ReplacesDocumentId!=null)throw new DocumentException(400,"InvalidEngineeringPdf");bytes=await EfEngineeringStore.ReadBounded(upload.Content,token);name=upload.FileName;EfEngineeringStore.ValidatePdf(name,bytes);}
        if(bytes==null)throw new DocumentException(400,"InvalidEngineeringPdf");var hash=Hash(bytes);var fp=Fingerprint(new{actaId,roundId,c,name,hash});
        await using var tx=await db.Database.BeginTransactionAsync(token);var(project,tasks,_)=await Lock(id,actaId,token);
        var round=await db.Set<CommercialOpeningRound>().AsNoTracking().SingleAsync(r=>r.Id==roundId&&r.OpeningRecordId==actaId&&r.ProjectId==id,token);
        var review=c.ReviewId==null?null:await db.Set<CommercialOpeningReview>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==c.ReviewId&&r.RoundId==roundId&&r.ProjectId==id,token);
        Authorize(project,round,review,tasks,actor,role,reviewing);if(await Replay(id,c.RequestKey,fp,actor,token)!=null)return;
        if(round.SupersededAt!=null||round.ResolvedAt!=null||round.PresentedAt==null)throw Conflict("OpeningRoundStateConflict");
        var rows=await db.Set<CommercialOpeningRow>().AsNoTracking().Where(r=>r.RoundId==roundId).ToArrayAsync(token);var chosen=Select(rows,c.Structures);var members=Array.Empty<CommercialOpeningReviewMember>();
        if(reviewing){if(review==null||review.CompletedAt!=null||!review.Declared)throw Conflict("OpeningAffectedStructuresRequired");Revision(review.Revision,c.ExpectedRevision);members=await db.Set<CommercialOpeningReviewMember>().AsNoTracking().Where(m=>m.ReviewId==review.Id).ToArrayAsync(token);if(chosen.Length!=rows.Length)throw Conflict("OpeningDocumentScopeConflict");}
        else if(chosen.Any(r=>r.Approval!="AwaitingApproval"||r.AcceptanceId!=null))throw Conflict("OpeningEvidenceConflict");
        var at=timing.UtcNow;var document=new ProjectDocument(Guid.NewGuid(),id,name!,"application/pdf",bytes.Length,c.RequestKey,at,actor.Subject,actor.Email,actor.Name,isEngineeringDocument:true);
        var declared=chosen.OrderBy(r=>r.StructureId).Select(r=>new{r.StructureId,EvidenceVersion=r.EvidenceVersion+(reviewing&&members.Any(m=>m.StructureId==r.StructureId&&m.Changed)?1:0),EvidenceDocumentId=reviewing&&members.Any(m=>m.StructureId==r.StructureId&&m.Changed)?document.Id:r.EvidenceDocumentId}).ToArray();
        var canonical=Json(declared);var scopeHash=Fingerprint(declared);var version=(await db.Set<OpeningDocumentScope>().Where(s=>s.OpeningRecordId==actaId&&s.Kind==c.Kind).MaxAsync(s=>(int?)s.Version,token)??0)+1;
        var scope=new OpeningDocumentScope(actaId,id,document.Id,c.Kind,version,hash,Json(new{RoundId=roundId,ReviewId=c.ReviewId,scopeHash,DeclaredAt=at,DeclaredBy=actor.Subject}));
        var link=new CommercialOpeningDocument(id,actaId,roundId,document.Id,c.Kind,c.ReviewId,canonical,scopeHash);
        var added=new List<object>{document,scope,link};added.AddRange(chosen.Select(r=>new OpeningDocumentMember(actaId,id,document.Id,r.StructureId)));
        added.AddRange(chosen.Select(r=>new CommercialOpeningDocumentMember(link,r,declared.Single(m=>m.StructureId==r.StructureId).EvidenceVersion)));
        db.Attach(round);round.Advance(at);if(review!=null){db.Attach(review);review.SetDocument(document.Id);}
        added.Add(new CommercialOpeningEvent(id,actaId,roundId,c.RequestKey,fp,"Upload"+c.Kind,document.Id,at,actor.Subject,Json(new{Command=c,DocumentId=document.Id,scopeHash,hash})));
        var commitAttempted=false;
        try{await files.SaveAsync(document.StorageKey,bytes,token);db.AddRange(added);await db.SaveChangesAsync(token);commitAttempted=true;await tx.CommitAsync(token);}
        catch{if(!commitAttempted){await tx.RollbackAsync(CancellationToken.None);await files.DeleteAsync(document.StorageKey,CancellationToken.None);}throw;}
        finally{foreach(var e in added.Append(round).Concat(review==null?[]:new object[]{review}))db.Entry(e).State=EntityState.Detached;}
    }
}
