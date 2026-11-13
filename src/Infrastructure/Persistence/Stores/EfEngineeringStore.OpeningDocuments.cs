using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfEngineeringStore
{
    public async Task UploadOpeningAsync(Guid id,Guid actaId,Guid key,long revision,string scopeHash,IAsyncEnumerable<DocumentUpload> uploads,DecisionActor actor,CancellationToken token)
    {
        await RequireNative(id,actor,token);await OwnedActa(id,actaId,actor,token);
        byte[]? bytes=null;string? name=null;
        await foreach(var upload in uploads.WithCancellation(token))
        { if(bytes!=null || upload.ReplacesDocumentId!=null) throw new DocumentException(400,"InvalidEngineeringPdf"); bytes=await ReadBounded(upload.Content,token);name=upload.FileName;ValidatePdf(name,bytes); }
        if(bytes==null) throw new DocumentException(400,"InvalidEngineeringPdf");
        var hash=Hash(bytes);var fingerprint=Fingerprint(new{action="UploadOpening",actaId,revision,scopeHash,name,hash});
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await LockProject(id,token);await RequireNative(id,actor,token);
        var state=await OwnedActa(id,actaId,actor,token);
        if(await WorkspaceReplay(id,key,fingerprint,actor,token)!=null) return;
        if(state.ConfirmedAt!=null) throw new DocumentException(409,"EngineeringLocked");Revision(state.Revision,revision);
        var members=await Members(id,actaId,token);
        if(members.Length==0 || ScopeHash(members)!=scopeHash) throw new DocumentException(409,"EngineeringDocumentScopeConflict");
        var at=timing.UtcNow;
        var doc=new ProjectDocument(Guid.NewGuid(),id,name!,"application/pdf",bytes.Length,key,at,actor.Subject,actor.Email,actor.Name,isEngineeringDocument:true);
        var version=(await db.Set<OpeningDocumentScope>().Where(s=>s.OpeningRecordId==actaId && s.Kind=="ENGINEERING").MaxAsync(s=>(int?)s.Version,token) ?? 0)+1;
        var scope=new OpeningDocumentScope(actaId,id,doc.Id,"ENGINEERING",version,hash,JsonSerializer.Serialize(new{ScopeHash=scopeHash,DeclaredAt=at,DeclaredBy=actor.Subject}));
        db.Attach(state);state.SetDocument(doc.Id);
        var added=new List<object>{doc,scope,new EngineeringWorkspaceEvent(id,key,fingerprint,"UploadOpening",actaId,state.Revision,at,actor.Subject,JsonSerializer.Serialize(new{DocumentId=doc.Id,version,hash,scopeHash}))};
        added.AddRange(members.Select(m=>new OpeningDocumentMember(actaId,id,doc.Id,m.StructureId)));
        var commitAttempted=false;
        try { await files.SaveAsync(doc.StorageKey,bytes,token);db.AddRange(added);await db.SaveChangesAsync(token);commitAttempted=true;await tx.CommitAsync(token); }
        catch { if(!commitAttempted) { await tx.RollbackAsync(CancellationToken.None);await files.DeleteAsync(doc.StorageKey,CancellationToken.None); } throw; }
        finally { foreach(var e in added.Append(state)) db.Entry(e).State=EntityState.Detached; }
    }
    public async Task<DocumentDownload> DownloadOpeningAsync(Guid id,Guid actaId,Guid documentId,ProjectReadScope scope,CancellationToken token)
    {
        if(!await CanRead(id,scope,token)) throw new DocumentException(403,"EngineeringAccessDenied");
        var file=await (from s in db.Set<OpeningDocumentScope>().AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on s.DocumentId equals d.Id
            where s.ProjectId==id && s.OpeningRecordId==actaId && s.DocumentId==documentId && s.Kind=="ENGINEERING" && d.ProjectId==id && d.IsEngineeringDocument select new{s,d}).SingleOrDefaultAsync(token) ?? throw new DocumentException(404,"DocumentNotFound");
        return new(new MemoryStream(await VerifiedBytes(file.d,file.s.Sha256,token),false),file.d.OriginalFileName);
    }
}
