using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Infrastructure.Persistence.Stores;

// A durable, bounded queue. Claim and completion are short transactions; PDF processing holds no DB locks.
public sealed class EfDocumentExtractionStore(SnGControlDbContext db,TimeProvider clock,IOptions<DocumentExtractionOptions> options,
    IStreamingFileStorage files,IDocumentComponentProcessor processor) : IDocumentExtractionStore
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private static JsonElement Json(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private static ExtractionJobView View(DocumentExtractionJob j)=>new(j.Id,j.PreparationId,j.ExtractorVersion,j.State.ToString(),j.Attempts,j.Revision,j.RequestedAt,j.CompletedAt,j.ErrorCode,Json(j.ReportJson),Json(j.EventsJson),j.Generation,j.ComponentRevisionId);
    public async Task<ExtractionJobView?> ReadAsync(Guid preparation,CancellationToken token)
    { var job=await db.Set<DocumentExtractionJob>().AsNoTracking().Where(j=>j.PreparationId==preparation&&j.ExtractorVersion==DocumentExtractorVersion.Current).OrderByDescending(j=>j.Generation).FirstOrDefaultAsync(token);return job==null?null:View(job); }
    public async Task<CandidatePage> CandidatesAsync(Guid preparation,int skip,CancellationToken token)
    {
        if(skip<0)throw new DocumentException(400,"InvalidOperationsPage");
        var rows=await db.Set<ProductionCandidateRow>().AsNoTracking().Where(r=>r.PreparationId==preparation).OrderBy(r=>r.DocumentId).ThenBy(r=>r.Page).ThenBy(r=>r.SourceRow).ThenBy(r=>r.Id).Skip(skip).Take(100).ToArrayAsync(token);
        return new(skip,rows.Select(r=>JsonSerializer.SerializeToElement(r,new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray());
    }
    public async Task<ExtractionJobView> QueueAsync(Guid preparation,string actor,CancellationToken token)
    {
        if(!options.Value.Enabled)throw new DocumentException(503,"ExtractionUnavailable");
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73122101)",token);
        var p=await db.Set<ProductionPreparation>().FromSqlInterpolated($"SELECT * FROM \"ProductionPreparations\" WHERE \"Id\"={preparation} FOR UPDATE").SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404,"OperationsPreparationNotFound");
        var job=await db.Set<DocumentExtractionJob>().Where(j=>j.PreparationId==preparation&&j.ExtractorVersion==DocumentExtractorVersion.Current).OrderByDescending(j=>j.Generation).FirstOrDefaultAsync(token);
        if(job==null) {
            if(await db.Set<ProductionReview>().AnyAsync(r=>r.PreparationId==preparation,token)||await db.Set<DocumentExtractionJob>().AnyAsync(j=>j.PreparationId==preparation,token))throw new DocumentException(409,"ExplicitReextractionRequired");
            job=new(preparation,DocumentExtractorVersion.Current,actor,Now);db.Add(job);}
        else if(await db.Set<ProductionReview>().AnyAsync(r=>r.ExtractionJobId==job.Id,token)){await tx.CommitAsync(token);return View(job);}
        else if(job.Retry(actor,Now)&&job.Generation==1)p.RecordExtraction(PreparationState.PendingExtraction,DocumentExtractorVersion.Current);
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return View(job);
    }
    public async Task<bool> RunOneAsync(CancellationToken token)
    {
        DocumentExtractionJob? job;Guid lease;
        await using(var tx=await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73122101)",token);
            var now=Now;
            job=await db.Set<DocumentExtractionJob>().FromSqlInterpolated($"SELECT * FROM \"DocumentExtractionJobs\" WHERE \"ExtractorVersion\"={DocumentExtractorVersion.Current} AND (\"State\"=1 OR (\"State\"=2 AND \"LeaseUntil\"<={now})) ORDER BY \"RequestedAt\",\"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").FirstOrDefaultAsync(token);
            if(job==null){await tx.CommitAsync(token);return false;}
            if(job.Attempts>=3)
            {
                job.Finish(ExtractionJobState.Failed,"{}","RecoveryExhausted",now);
                var failed=await db.Set<ProductionPreparation>().SingleAsync(p=>p.Id==job.PreparationId,token);if(job.Generation==1)failed.RecordExtraction(PreparationState.Blocked,job.ExtractorVersion);
                await db.SaveChangesAsync(token);await tx.CommitAsync(token);return true;
            }
            lease=job.Claim(now,options.Value.TimeoutSeconds+60);await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        }
        var id=job.Id;var preparation=job.PreparationId;db.ChangeTracker.Clear();
        JsonElement report;string? failure=null;bool transient=false;
        try {report=await ExtractAsync(preparation,token);}
        catch(ExtractionProcessingException e){failure=e.Code;transient=e.Code.StartsWith("Processor",StringComparison.Ordinal);report=JsonSerializer.SerializeToElement(new {version=DocumentExtractorVersion.Current,documents=Array.Empty<object>(),issues=new[]{new{code=e.Code,severity="error"}}});}
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;} // Lease remains recoverable after shutdown.
        catch(Exception e) when(e is IOException or InvalidOperationException or KeyNotFoundException or JsonException or FormatException or OverflowException or ArgumentException)
        {failure="SourceOrContractInvalid";report=JsonSerializer.SerializeToElement(new {version=DocumentExtractorVersion.Current,documents=Array.Empty<object>(),issues=new[]{new{code=failure,severity="error"}}});}
        await CompleteAsync(id,lease,report,failure,transient,token);return true;
    }
    private async Task<JsonElement> ExtractAsync(Guid id,CancellationToken token)
    {
        var p=await db.Set<ProductionPreparation>().AsNoTracking().SingleAsync(p=>p.Id==id,token);
        var release=await db.Set<OpeningFilingRelease>().AsNoTracking().SingleAsync(r=>r.VersionId==p.ReleaseVersionId,token);
        var version=await db.Set<OpeningFilingVersion>().AsNoTracking().SingleAsync(v=>v.Id==p.ReleaseVersionId,token);
        var members=await db.Set<OpeningFilingMember>().AsNoTracking().Where(m=>m.VersionId==p.ReleaseVersionId).ToArrayAsync(token);
        var links=await db.Set<OpeningFilingDocument>().AsNoTracking().Where(d=>d.VersionId==p.ReleaseVersionId&&d.State=="Current").ToArrayAsync(token);
        if(release.Sha256!=p.ManifestSha256||!EfProductionPreparationStore.ValidSource(release,version,members,links))throw new ExtractionProcessingException("ReleaseIntegrityInvalid");
        var manifest=Json(release.Canonical);var docs=new List<ExtractionInputDocument>();
        foreach(var kind in new[]{("UnoptimizedCutList","aluminum"),("Glass","glass")})
        {
            var link=links.SingleOrDefault(d=>d.Category==kind.Item1)??throw new ExtractionProcessingException("DocumentSetInvalid");
            var evidence=manifest.GetProperty("Documents").EnumerateArray().Single(d=>d.GetProperty("Id").GetGuid()==link.DocumentId);
            var document=await db.ProjectDocuments.AsNoTracking().SingleAsync(d=>d.Id==link.DocumentId&&d.ProjectId==p.ProjectId,token);
            var size=evidence.GetProperty("SizeBytes").GetInt64();
            if(size!=document.SizeBytes||size<1||size>options.Value.MaxDocumentBytes)throw new ExtractionProcessingException("DocumentSizeInvalid");
            await using var source=await files.OpenReadAsync(document.StorageKey,token);
            using var memory=new MemoryStream((int)size);var buffer=new byte[65536];int count;
            while((count=await source.ReadAsync(buffer,token))>0)
            {if(memory.Length+count>size)throw new ExtractionProcessingException("DocumentSizeInvalid");await memory.WriteAsync(buffer.AsMemory(0,count),token);}
            var bytes=memory.ToArray();
            if(bytes.LongLength!=size||Convert.ToHexString(SHA256.HashData(bytes))!=link.Sha256)throw new ExtractionProcessingException("DocumentHashInvalid");
            docs.Add(new(document.Id,kind.Item2,link.Sha256,size,bytes));
        }
        var input=new ExtractionInput(p.ManifestSha256,members.Select(m=>m.OpNormalized).ToArray(),options.Value.MaxPages,docs.ToArray());
        var output=await processor.ProcessAsync(input,token);
        ValidateReport(output,input);return output;
    }
    private static void ValidateReport(JsonElement report,ExtractionInput input)
    {
        if(report.GetProperty("version").GetString()!=DocumentExtractorVersion.Current)throw new ExtractionProcessingException("ProcessorContractInvalid");
        var docs=report.GetProperty("documents").EnumerateArray().ToArray();
        if(docs.Length!=2||docs.Select(d=>d.GetProperty("documentId").GetGuid()).Distinct().Count()!=2)throw new ExtractionProcessingException("ProcessorContractInvalid");
        foreach(var d in docs)
        {
            var original=input.Documents.Single(x=>x.Id==d.GetProperty("documentId").GetGuid());
            if(d.GetProperty("sha256").GetString()!=original.Sha256||d.GetProperty("kind").GetString()!=original.Kind||d.GetProperty("rows").GetArrayLength()>10000)throw new ExtractionProcessingException("ProcessorContractInvalid");
            foreach(var issue in d.GetProperty("issues").EnumerateArray())if(issue.GetProperty("severity").GetString() is not ("error" or "warning"))throw new ExtractionProcessingException("ProcessorContractInvalid");
            var locations=new HashSet<string>();
            foreach(var row in d.GetProperty("rows").EnumerateArray())
            {
                if(row.GetProperty("documentSha256").GetString()!=original.Sha256||!locations.Add(row.GetProperty("source").GetRawText()))throw new ExtractionProcessingException("ProcessorContractInvalid");
                var source=row.GetProperty("source");
                if(row.GetProperty("quantity").GetInt32() is <1 or >100000 || source.GetProperty("page").GetInt32() is <1 || source.GetProperty("page").GetInt32()>input.MaxPages || source.GetProperty("line").GetInt32()<1)
                    throw new ExtractionProcessingException("ProcessorContractInvalid");
                foreach(var field in new[]{("op",80),("reference",200),("description",4000),("pieceId",200),("dimensionUnit",30),("originalDimensions",1000)})
                    if(row.TryGetProperty(field.Item1,out var value)&&value.ValueKind!=JsonValueKind.Null&&(value.ValueKind!=JsonValueKind.String||value.GetString()!.Length>field.Item2))throw new ExtractionProcessingException("ProcessorContractInvalid");
                foreach(var field in new[]{"measure","ct","width","height","leftAngle","rightAngle"})
                    if(row.TryGetProperty(field,out var value)&&value.ValueKind!=JsonValueKind.Null&&(value.ValueKind!=JsonValueKind.String||!decimal.TryParse(value.GetString(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out _)))throw new ExtractionProcessingException("ProcessorContractInvalid");
            }
        }
    }
    private async Task CompleteAsync(Guid id,Guid lease,JsonElement report,string? failure,bool transient,CancellationToken token)
    {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73122101)",token);
        var job=await db.Set<DocumentExtractionJob>().FromSqlInterpolated($"SELECT * FROM \"DocumentExtractionJobs\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(token);
        if(job.State!=ExtractionJobState.Running||job.LeaseToken!=lease||job.LeaseUntil<=Now||await db.Set<ProductionReview>().AnyAsync(r=>r.ExtractionJobId==job.Id,token)){await tx.RollbackAsync(token);return;}
        var p=await db.Set<ProductionPreparation>().SingleAsync(p=>p.Id==job.PreparationId,token);
        var documents=report.GetProperty("documents").EnumerateArray().ToArray();
        var blocked=failure!=null||documents.Any(d=>d.GetProperty("issues").EnumerateArray().Any(i=>i.GetProperty("severity").GetString()=="error"));
        var warnings=documents.Any(d=>d.GetProperty("issues").GetArrayLength()>0);
        var members=await db.Set<PreparationSourceMember>().Where(m=>m.PreparationId==p.Id).ToArrayAsync(token);
        // Independently enforce OP membership even if a processor omits its diagnostic.
        if(documents.SelectMany(d=>d.GetProperty("rows").EnumerateArray()).Any(r=>!members.Any(m=>m.Op==r.GetProperty("op").GetString())))blocked=true;
        var state=blocked?PreparationState.Blocked:warnings?PreparationState.Extracted:PreparationState.ReadyForReview;
        if(!transient)
        {
            var number=(await db.Set<ProductionComponentRevision>().Where(r=>r.PreparationId==p.Id).MaxAsync(r=>(int?)r.Number,token)??0)+1;
            var revision=new ProductionComponentRevision(p,number,Now,job.RequestedBySubject);revision.RecordExtraction(state);db.Add(revision);job.SetResultRevision(revision.Id);
            var sources=await db.Set<PreparationSourceDocument>().Where(d=>d.PreparationId==p.Id).ToArrayAsync(token);
            foreach(var document in documents)
            {
                var source=sources.Single(d=>d.DocumentId==document.GetProperty("documentId").GetGuid());
                foreach(var row in document.GetProperty("rows").EnumerateArray())
                {
                    var member=members.SingleOrDefault(m=>m.Op==row.GetProperty("op").GetString());
                    if(member==null||row.GetProperty("quantity").GetInt32()<1)continue; // Rejected rows remain in the report, never with fabricated StructureId.
                    var aluminum=document.GetProperty("kind").GetString()=="aluminum";
                    string? S(string name)=>row.TryGetProperty(name,out var v)&&v.ValueKind!=JsonValueKind.Null?v.GetString():null;
                    decimal? N(string name)=>S(name) is {} v?decimal.Parse(v,CultureInfo.InvariantCulture):null;
                    var origin=row.GetProperty("source");
                    var data=new CandidateTechnicalData(aluminum?ComponentMaterial.AluminumProfile:null,aluminum?"aluminum profile":S("reference"),S("reference"),S("description"),S("originalDimensions"),N("measure"),N("ct"),N("width"),N("height"),S("dimensionUnit"),N("leftAngle"),N("rightAngle"),row.GetProperty("quantity").GetInt32(),S("pieceId"),row.GetRawText(),"[]");
                    db.Add(new ProductionCandidateRow(revision,member,source,data,new CandidateOrigin(origin.GetProperty("page").GetInt32(),origin.GetProperty("line").GetInt32().ToString(CultureInfo.InvariantCulture),null,origin.GetRawText(),document.GetProperty("template").GetString()!,job.ExtractorVersion)));
                }
            }
        }
        if(job.Generation==1)p.RecordExtraction(state,job.ExtractorVersion);
        job.Finish(transient?ExtractionJobState.Failed:blocked?ExtractionJobState.Blocked:ExtractionJobState.Completed,report.GetRawText(),failure,Now);
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
}
