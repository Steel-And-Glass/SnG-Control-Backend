using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfProductionReviewStore
{
    public async Task<JsonElement> UploadAsync(Guid prep,Guid request,string name,string? mime,byte[] bytes,string actor,CancellationToken token)
    {
        if(request==Guid.Empty||bytes.Length is <1 or >26214400)throw new DocumentException(400,"ReviewEvidenceInvalid");
        try{ProjectDocument.ValidateFileName(name);}catch(ArgumentException){throw new DocumentException(400,"ReviewEvidenceInvalid");}
        mime=mime is {Length:>0 and <=255}&&!mime.Any(char.IsControl)?mime:null;
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        await using var tx=await db.Database.BeginTransactionAsync(token);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73122101)",token);
        if(!await db.Set<ProductionPreparation>().AnyAsync(p=>p.Id==prep,token))throw new DocumentException(404,"OperationsPreparationNotFound");
        var found=await db.Set<ProductionReviewAttachment>().SingleOrDefaultAsync(a=>a.PreparationId==prep&&a.RequestKey==request,token);
        if(found!=null){if(found.Sha256!=hash||found.Name!=name||found.ContentType!=mime||found.Actor!=actor)throw Conflict("ReviewIdempotencyConflict");return Json(found);}
        if(await db.Set<ProductionApproval>().AnyAsync(a=>a.PreparationId==prep,token))throw Conflict("PreparationAlreadyApproved");
        if(await db.Set<ProductionReviewAttachment>().CountAsync(a=>a.PreparationId==prep,token)>=50||(await db.Set<ProductionReviewAttachment>().Where(a=>a.PreparationId==prep).SumAsync(a=>(long?)a.SizeBytes,token)??0)+bytes.LongLength>262144000)throw Conflict("ReviewEvidenceQuotaExceeded");
        var file=new ProductionReviewAttachment(prep,request,name,mime,bytes.LongLength,hash,Now,actor);
        var saved=false;var committing=false;
        try
        {
            await files.SaveAsync(file.Key(),bytes,token);saved=true;db.Add(file);await db.SaveChangesAsync(token);
            committing=true;await tx.CommitAsync(token);return Json(file);
        }
        catch
        {
            if(!committing){await tx.RollbackAsync(CancellationToken.None);if(saved)await files.DeleteAsync(file.Key(),CancellationToken.None);}
            // Unknown commit outcome: retain bytes; never risk deleting committed evidence.
            throw;
        }
    }
    public async Task<ReviewDocument> DocumentAsync(Guid prep,Guid id,bool attachment,CancellationToken token)
    {
        string key,name,hash;long size;
        if(attachment)
        {var file=await db.Set<ProductionReviewAttachment>().AsNoTracking().SingleOrDefaultAsync(a=>a.PreparationId==prep&&a.Id==id,token)??throw new DocumentException(404,"ReviewEvidenceNotFound");key=file.Key();name=file.Name;hash=file.Sha256;size=file.SizeBytes;}
        else
        {
            var link=await db.Set<PreparationSourceDocument>().AsNoTracking().SingleOrDefaultAsync(d=>d.PreparationId==prep&&d.DocumentId==id,token)??throw new DocumentException(404,"ReviewEvidenceNotFound");
            var file=await db.ProjectDocuments.AsNoTracking().SingleAsync(d=>d.Id==id,token);key=file.StorageKey;name=file.OriginalFileName;hash=link.Sha256;size=file.SizeBytes;
        }
        if(size is <1 or >26214400)throw Conflict("ReviewDocumentIntegrityInvalid");
        // Validate the same byte array returned, avoiding a separate-check/read race.
        await using var stream=await files.OpenReadAsync(key,token);using var memory=new MemoryStream((int)size);var buffer=new byte[65536];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(memory.Length+count>size)throw Conflict("ReviewDocumentIntegrityInvalid");await memory.WriteAsync(buffer.AsMemory(0,count),token);}
        var bytes=memory.ToArray();if(bytes.LongLength!=size||Convert.ToHexString(SHA256.HashData(bytes))!=hash)throw Conflict("ReviewDocumentIntegrityInvalid");
        return new(bytes,name,"application/octet-stream");
    }
}
