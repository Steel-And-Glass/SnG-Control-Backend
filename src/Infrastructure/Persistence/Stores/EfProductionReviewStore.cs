using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities.Production;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfProductionReviewStore(SnGControlDbContext db,TimeProvider clock,IStreamingFileStorage files,IOptions<DocumentExtractionOptions> extractionOptions):IProductionReviewStore
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private static readonly JsonSerializerOptions Web=new(JsonSerializerDefaults.Web);
    private static JsonElement Json<T>(T value)=>JsonSerializer.SerializeToElement(value,Web);
    private static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Web);
    private static DocumentException Conflict(string code)=>new(409,code);
    public async Task<IReadOnlyList<JsonElement>> ListAsync(Guid prep,CancellationToken token)=>
        (await db.Set<ProductionReview>().AsNoTracking().Where(r=>r.PreparationId==prep).OrderBy(r=>r.Number).Take(100).ToArrayAsync(token)).Select(Json).ToArray();
    public async Task<ReviewView?> ReadAsync(Guid prep,Guid id,CancellationToken token)
    {
        var review=await db.Set<ProductionReview>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id&&r.PreparationId==prep,token);if(review==null)return null;
        var rows=await db.Set<ProductionReviewRow>().AsNoTracking().Where(r=>r.ReviewId==id).OrderBy(r=>r.Op).ThenBy(r=>r.Id).ToArrayAsync(token);
        var issues=await db.Set<ProductionReviewIssue>().AsNoTracking().Where(i=>i.ReviewId==id).OrderBy(i=>i.Id).ToArrayAsync(token);
        var approval=await db.Set<ProductionApproval>().AsNoTracking().SingleOrDefaultAsync(a=>a.ReviewId==id,token);
        var preparation=await db.Set<ProductionPreparation>().AsNoTracking().SingleAsync(p=>p.Id==prep,token);
        var job=await db.Set<DocumentExtractionJob>().AsNoTracking().SingleAsync(j=>j.Id==review.ExtractionJobId,token);
        var attachments=await db.Set<ProductionReviewAttachment>().AsNoTracking().Where(a=>a.PreparationId==prep).OrderBy(a=>a.At).ThenBy(a=>a.Id).ToArrayAsync(token);
        return new(Json(review),rows.Select(r=>Json(new{r.Id,r.CandidateId,r.StructureId,r.Op,r.DocumentId,r.DocumentSha256,original=JsonSerializer.Deserialize<JsonElement>(r.OriginalJson),values=r.Values(),r.Reviewed,r.Excluded})).ToArray(),issues.Select(i=>Json(new{i.Id,i.Code,i.Category,evidence=JsonSerializer.Deserialize<JsonElement>(i.EvidenceJson),resolved=i.Category=="HumanReview"&&i.ResolvedDataRevision==review.DataRevision})).ToArray(),approval==null?null:Json(approval),new(preparation.ProjectId,preparation.FilingId,preparation.ReleaseVersionId,preparation.ManifestSha256,job.ExtractorVersion),attachments.Select(Json).ToArray());
    }
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(Guid prep,int skip,CancellationToken token)
    {if(skip<0)throw new DocumentException(400,"InvalidOperationsPage");return (await db.Set<ProductionReviewEvent>().FromSqlInterpolated($"SELECT e.* FROM \"ProductionReviewEvents\" e JOIN \"ProductionReviews\" r ON r.\"Id\"=e.\"ReviewId\" WHERE e.\"PreparationId\"={prep} ORDER BY r.\"Number\", (e.\"ResultJson\"->>'revision')::bigint LIMIT 100 OFFSET {skip}").AsNoTracking().ToArrayAsync(token)).Select(Json).ToArray();}
    private async Task Evidence(Guid prep,ReviewEvidence? evidence,CancellationToken token)
    {
        if(evidence==null||evidence.DocumentId.HasValue==evidence.AttachmentId.HasValue||string.IsNullOrWhiteSpace(evidence.Locator)||evidence.Locator.Length>500||string.IsNullOrWhiteSpace(evidence.Observation)||evidence.Observation.Length>2000)throw new DocumentException(400,"ReviewEvidenceRequired");
        if(evidence.DocumentId is {} doc){if(!await db.Set<PreparationSourceDocument>().AnyAsync(d=>d.PreparationId==prep&&d.DocumentId==doc,token))throw Conflict("ReviewEvidenceOutsideRelease");}
        else if(!await db.Set<ProductionReviewAttachment>().AnyAsync(a=>a.PreparationId==prep&&a.Id==evidence.AttachmentId,token))throw Conflict("ReviewEvidenceOutsidePreparation");
    }
    public async Task<ReviewCommandResult> CommandAsync(Guid prep,ProductionReviewCommand c,string actor,CancellationToken token)
    {
        if(c.RequestKey==Guid.Empty||c.ExpectedRevision<0||string.IsNullOrWhiteSpace(c.Reason)||c.Reason.Length>2000||c.Action is not ("Start" or "Edit" or "Add" or "Exclude" or "Restore" or "Resolve" or "Note" or "RecordConflict" or "Approve" or "Reextract"))throw new DocumentException(400,"ReviewCommandInvalid");
        var fingerprint=IncrementalEvidence.Hash(Serialize(c));
        await using var tx=await db.Database.BeginTransactionAsync(token);await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73122101)",token);
        var p=await db.Set<ProductionPreparation>().SingleOrDefaultAsync(p=>p.Id==prep,token)??throw new DocumentException(404,"OperationsPreparationNotFound");
        var existing=await db.Set<ProductionReviewEvent>().SingleOrDefaultAsync(e=>e.PreparationId==prep&&e.RequestKey==c.RequestKey,token);
        if(existing!=null){if(existing.Fingerprint!=fingerprint||existing.Actor!=actor)throw Conflict("ReviewIdempotencyConflict");return JsonSerializer.Deserialize<ReviewCommandResult>(existing.ResultJson,Web)!;}
        ProductionReview review;object? before=null;object? after=null;Guid? approvalId=null;Guid? jobId=null;
        if(c.Action=="Start")
        {
            if(c.ReviewId!=null||c.ExtractionJobId==null||c.ExpectedRevision!=0)throw new DocumentException(400,"ReviewCommandInvalid");
            if(await db.Set<ProductionApproval>().AnyAsync(a=>a.PreparationId==prep,token))throw Conflict("PreparationAlreadyApproved");
            var job=await db.Set<DocumentExtractionJob>().SingleOrDefaultAsync(j=>j.Id==c.ExtractionJobId&&j.PreparationId==prep,token)??throw Conflict("ReviewExtractionUnavailable");
            if(job.State is not (ExtractionJobState.Completed or ExtractionJobState.Blocked)||await db.Set<DocumentExtractionJob>().AnyAsync(j=>j.PreparationId==prep&&j.Generation>job.Generation,token))throw Conflict("ReviewExtractionUnavailable");
            if(await db.Set<ProductionReview>().AnyAsync(r=>r.ExtractionJobId==job.Id,token))throw Conflict("ReviewAlreadyExists");
            var source=await SourceRevision(job,token);
            var number=(await db.Set<ProductionReview>().Where(r=>r.PreparationId==prep).MaxAsync(r=>(int?)r.Number,token)??0)+1;
            review=new(prep,job.Id,source,number,Now,actor);db.Add(review);
            var candidates=await db.Set<ProductionCandidateRow>().Where(r=>r.ComponentRevisionId==source&&r.PreparationId==prep).ToArrayAsync(token);
            if(candidates.Length>10000)throw Conflict("ReviewScopeTooLarge");
            foreach(var row in candidates)db.Add(new ProductionReviewRow(review,row.Id,row.StructureId,row.Op,row.DocumentId,row.DocumentSha256,Serialize(row),new(row.Material,row.ProposedRoute,row.Reference,row.Description,row.Measure,row.CutDimension,row.Width,row.Height,row.Unit,row.LeftAngle,row.RightAngle,row.Quantity)));
            using var report=JsonDocument.Parse(job.ReportJson);var root=report.RootElement;
            var issueCount=0;
            void Issues(JsonElement array,Guid? doc){foreach(var issue in array.EnumerateArray()){issueCount++;var code=issue.GetProperty("code").GetString()!;db.Add(new ProductionReviewIssue(review.Id,code,ReviewDiagnosticPolicy.Category(code),Serialize(new{documentId=doc,issue})));}}
            if(root.TryGetProperty("issues",out var global))Issues(global,null);
            if(root.TryGetProperty("documents",out var documents))foreach(var doc in documents.EnumerateArray())Issues(doc.GetProperty("issues"),doc.GetProperty("documentId").GetGuid());
            if(job.State==ExtractionJobState.Blocked&&issueCount==0)db.Add(new ProductionReviewIssue(review.Id,"EXTRACTION_INCOMPLETE","IntegrityOrScope","{}"));
            // Re-extraction of the same release cannot wash away formal/integrity conflicts recorded by humans.
            var inherited=await (from i in db.Set<ProductionReviewIssue>() join prior in db.Set<ProductionReview>() on i.ReviewId equals prior.Id where prior.PreparationId==prep && i.Category!="HumanReview" select i).AsNoTracking().ToArrayAsync(token);
            foreach(var unresolved in inherited.DistinctBy(i=>(i.Code,i.EvidenceJson)))db.Add(new ProductionReviewIssue(review.Id,unresolved.Code,unresolved.Category,unresolved.EvidenceJson));
            after=new{source,job.Id,candidates=candidates.Select(x=>x.Id).Order().ToArray()};
        }
        else
        {
            review=await db.Set<ProductionReview>().SingleOrDefaultAsync(r=>r.Id==c.ReviewId&&r.PreparationId==prep,token)??throw new DocumentException(404,"ReviewNotFound");
            if(review.State!=ProductionReviewState.Draft)throw Conflict("ReviewFrozen");
            if(review.Revision!=c.ExpectedRevision)throw Conflict("ReviewRevisionConflict");
            if(await db.Set<DocumentExtractionJob>().AnyAsync(j=>j.PreparationId==prep&&(j.State==ExtractionJobState.Queued||j.State==ExtractionJobState.Running),token))throw Conflict("ExtractionStillRunning");
            if(c.Action is "Edit" or "Add" or "Exclude" or "Restore" or "Resolve" or "Note" or "RecordConflict")await Evidence(prep,c.Evidence,token);
            switch(c.Action)
            {
                case "Edit":case "Exclude":case "Restore":
                    var row=await db.Set<ProductionReviewRow>().SingleOrDefaultAsync(r=>r.Id==c.RowId&&r.ReviewId==review.Id,token)??throw Conflict("ReviewRowNotFound");
                    before=Json(row);
                    if(c.Action=="Edit"){if(c.Values==null)throw new DocumentException(400,"ReviewValuesRequired");ReviewDiagnosticPolicy.Validate(c.Values,false);row.Edit(c.Values);}else row.Exclude(c.Action=="Exclude");
                    review.Change(true);after=Json(row);break;
                case "Add":
                    if(c.Values==null||c.StructureId==null||c.Op==null||c.SourceDocumentId==null)throw new DocumentException(400,"ReviewValuesRequired");
                    ReviewDiagnosticPolicy.Validate(c.Values,false);
                    var member=await db.Set<PreparationSourceMember>().SingleOrDefaultAsync(m=>m.PreparationId==prep&&m.StructureId==c.StructureId&&m.Op==c.Op,token)??throw Conflict("ReviewOpOutsideRelease");
                    var doc=await db.Set<PreparationSourceDocument>().SingleOrDefaultAsync(d=>d.PreparationId==prep&&d.DocumentId==c.SourceDocumentId,token)??throw Conflict("ReviewEvidenceOutsideRelease");
                    if(await db.Set<ProductionReviewRow>().CountAsync(r=>r.ReviewId==review.Id,token)>=10000)throw Conflict("ReviewScopeTooLarge");
                    var manual=new ProductionReviewRow(review,null,member.StructureId,member.Op,doc.DocumentId,doc.Sha256,Serialize(new{manual=true,quantity=(int?)null,evidence=c.Evidence}),c.Values);manual.Edit(c.Values);db.Add(manual);review.Change(true);after=Json(manual);break;
                case "Resolve":
                    var issue=await db.Set<ProductionReviewIssue>().SingleOrDefaultAsync(i=>i.Id==c.IssueId&&i.ReviewId==review.Id,token)??throw Conflict("ReviewIssueNotFound");
                    if(issue.Category!="HumanReview")throw Conflict("ReviewResolutionPolicyMissing");
                    var included=await db.Set<ProductionReviewRow>().Where(r=>r.ReviewId==review.Id&&!r.Excluded).ToArrayAsync(token);
                    if(included.Length==0||included.Any(r=>!r.Reviewed))throw Conflict("ReviewIncomplete");
                    foreach(var item in included)ReviewDiagnosticPolicy.Validate(item.Values(),true);
                    before=Json(issue);issue.Resolve(review.DataRevision);review.Change();after=Json(issue);break;
                case "RecordConflict":
                    if(c.ConflictCode is not ("TOTAL_MISMATCH" or "SOURCE_DIMENSION_CONFLICT"))throw new DocumentException(400,"ReviewConflictCodeInvalid");
                    var conflict=new ProductionReviewIssue(review.Id,c.ConflictCode,"FormalDocumentResolution",Serialize(c.Evidence));db.Add(conflict);review.Change();after=Json(conflict);break;
                case "Note":review.Change();after=c.Evidence;break;
                case "Reextract":
                    if(!extractionOptions.Value.Enabled)throw new DocumentException(503,"ExtractionUnavailable");
                    if(await db.Set<ProductionApproval>().AnyAsync(a=>a.PreparationId==prep,token))throw Conflict("PreparationAlreadyApproved");
                    var generation=(await db.Set<DocumentExtractionJob>().Where(j=>j.PreparationId==prep).MaxAsync(j=>(int?)j.Generation,token)??0)+1;
                    var next=new DocumentExtractionJob(prep,DocumentExtractorVersion.Current,actor,Now,generation,c.Reason);db.Add(next);review.Supersede();jobId=next.Id;after=new{next.Id,next.Generation};break;
                case "Approve":
                    approvalId=await Approve(p,review,actor,token);after=new{approvalId};break;
            }
        }
        var result=new ReviewCommandResult(review.Id,review.Revision,approvalId,jobId);
        db.Add(new ProductionReviewEvent(prep,review.Id,c.RequestKey,fingerprint,c.Action,c.Reason,Now,actor,Serialize(new{before,after,evidence=c.Evidence}),Serialize(result)));
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return result;
    }
    private async Task<Guid> SourceRevision(DocumentExtractionJob job,CancellationToken token)
    {
        if(job.ComponentRevisionId is {} bound)return bound;
        // P2.2 historic jobs have no result FK. Only accept a unique exact report-row match; never guess latest.
        using var json=JsonDocument.Parse(job.ReportJson);if(!json.RootElement.TryGetProperty("documents",out var docs))throw Conflict("ExtractionProvenanceIncomplete");
        var expected=docs.EnumerateArray().SelectMany(d=>d.GetProperty("rows").EnumerateArray().Select(row=>(Doc:d.GetProperty("documentId").GetGuid(),Op:row.GetProperty("op").GetString(),Page:row.GetProperty("source").GetProperty("page").GetInt32(),Line:row.GetProperty("source").GetProperty("line").GetInt32().ToString(),Qty:row.GetProperty("quantity").GetInt32(),Raw:row.Clone()))).ToArray();
        var members=await db.Set<PreparationSourceMember>().Where(m=>m.PreparationId==job.PreparationId).Select(m=>m.Op).ToArrayAsync(token);expected=expected.Where(e=>members.Contains(e.Op)&&e.Qty>0).ToArray();
        var rows=await db.Set<ProductionCandidateRow>().Where(r=>r.PreparationId==job.PreparationId&&r.ExtractorVersion==job.ExtractorVersion).ToArrayAsync(token);
        var matches=rows.GroupBy(r=>r.ComponentRevisionId).Where(g=>g.Count()==expected.Length&&g.All(r=>expected.Any(e=>e.Doc==r.DocumentId&&e.Op==r.Op&&e.Page==r.Page&&e.Line==r.SourceRow&&e.Qty==r.Quantity&&JsonElement.DeepEquals(e.Raw,JsonSerializer.Deserialize<JsonElement>(r.SpecificationsJson))))).Select(g=>g.Key).ToArray();
        if(matches.Length!=1)throw Conflict("ExtractionProvenanceIncomplete");return matches[0];
    }
    private async Task<Guid> Approve(ProductionPreparation p,ProductionReview review,string actor,CancellationToken token)
    {
        if(await db.Set<ProductionApproval>().AnyAsync(a=>a.PreparationId==p.Id,token))throw Conflict("PreparationAlreadyApproved");
        var issues=await db.Set<ProductionReviewIssue>().Where(i=>i.ReviewId==review.Id).ToArrayAsync(token);
        if(issues.Any(i=>i.Category!="HumanReview"||i.ResolvedDataRevision!=review.DataRevision))throw Conflict("ReviewBlockingDiagnostics");
        var rows=await db.Set<ProductionReviewRow>().Where(r=>r.ReviewId==review.Id).OrderBy(r=>r.Id).ToArrayAsync(token);var included=rows.Where(r=>!r.Excluded).ToArray();
        if(included.Length==0||rows.Any(r=>!r.Reviewed))throw Conflict("ReviewIncomplete");
        foreach(var row in included)ReviewDiagnosticPolicy.Validate(row.Values(),true);
        var members=await db.Set<PreparationSourceMember>().Where(m=>m.PreparationId==p.Id).ToArrayAsync(token);
        if(!members.Select(m=>m.StructureId).ToHashSet().SetEquals(included.Select(r=>r.StructureId))||rows.Any(r=>!members.Any(m=>m.StructureId==r.StructureId&&m.Op==r.Op)))throw Conflict("ReviewOpCoverageIncomplete");
        await VerifyRelease(p,token);
        // Attached observations are also frozen by hashes; no client integrity assertion is trusted.
        var attachments=await db.Set<ProductionReviewAttachment>().Where(a=>a.PreparationId==p.Id).OrderBy(a=>a.Id).ToArrayAsync(token);
        foreach(var file in attachments)await VerifyBytes(file.Key(),file.SizeBytes,file.Sha256,token);
        var source=await db.Set<DocumentExtractionJob>().SingleAsync(j=>j.Id==review.ExtractionJobId,token);
        var at=Now;review.Approve();
        var canonical=Serialize(new{schema="production-approval-v1",p.ProjectId,p.FilingId,p.ReleaseVersionId,p.ManifestSha256,review.Id,review.Number,review.Revision,review.ExtractionJobId,source.ExtractorVersion,extractionReportHash=IncrementalEvidence.Hash(source.ReportJson),rows=rows.Select(r=>new{r.Id,r.CandidateId,r.StructureId,r.Op,r.DocumentId,r.DocumentSha256,original=JsonSerializer.Deserialize<JsonElement>(r.OriginalJson),corrected=r.Values(),r.Excluded}),resolutions=issues.OrderBy(i=>i.Id).Select(i=>new{i.Id,i.Code,i.ResolvedDataRevision}),attachments=attachments.Select(a=>new{a.Id,a.Sha256,a.SizeBytes}),actor,at});
        var approval=new ProductionApproval(review,canonical,at,actor);db.Add(approval);return approval.Id;
    }
    private async Task VerifyRelease(ProductionPreparation p,CancellationToken token)
    {
        var release=await db.Set<OpeningFilingRelease>().AsNoTracking().SingleAsync(r=>r.VersionId==p.ReleaseVersionId,token);
        var version=await db.Set<OpeningFilingVersion>().AsNoTracking().SingleAsync(v=>v.Id==p.ReleaseVersionId,token);
        var members=await db.Set<OpeningFilingMember>().AsNoTracking().Where(m=>m.VersionId==p.ReleaseVersionId).ToArrayAsync(token);
        var documents=await db.Set<OpeningFilingDocument>().AsNoTracking().Where(d=>d.VersionId==p.ReleaseVersionId&&d.State=="Current").ToArrayAsync(token);
        if(release.Sha256!=p.ManifestSha256||!EfProductionPreparationStore.ValidSource(release,version,members,documents))throw Conflict("ReviewReleaseIntegrityInvalid");
        using var manifest=JsonDocument.Parse(release.Canonical);
        foreach(var d in manifest.RootElement.GetProperty("Documents").EnumerateArray())
        {
            var id=d.GetProperty("Id").GetGuid();var file=await db.ProjectDocuments.AsNoTracking().SingleAsync(f=>f.Id==id&&f.ProjectId==p.ProjectId,token);
            if(file.SizeBytes!=d.GetProperty("SizeBytes").GetInt64())throw Conflict("ReviewDocumentIntegrityInvalid");
            await VerifyBytes(file.StorageKey,file.SizeBytes,d.GetProperty("Sha256").GetString()!,token);
        }
    }
    private async Task VerifyBytes(string key,long size,string hash,CancellationToken token)
    {
        try
        {
            await using var stream=await files.OpenReadAsync(key,token);using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long read=0;int count;
            while((count=await stream.ReadAsync(buffer,token))>0){read+=count;if(read>size)throw Conflict("ReviewDocumentIntegrityInvalid");sha.AppendData(buffer,0,count);}
            if(read!=size||Convert.ToHexString(sha.GetHashAndReset())!=hash)throw Conflict("ReviewDocumentIntegrityInvalid");
        }
        catch(IOException){throw Conflict("ReviewDocumentIntegrityInvalid");}
    }
}
