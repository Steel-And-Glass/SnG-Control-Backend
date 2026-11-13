using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Domain.Entities.Production;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Persistence;
internal static class DocumentExtractionHttpChecks
{
    private sealed class Processor(Func<ExtractionInput,Task<JsonElement>> run):IDocumentComponentProcessor
    { public Task<JsonElement> ProcessAsync(ExtractionInput input,CancellationToken token)=>run(input); }
    internal static JsonElement Report(ExtractionInput input)=>JsonSerializer.SerializeToElement(new {
        version=DocumentExtractorVersion.Current,documents=input.Documents.Select(d=>new {
            documentId=d.Id,sha256=d.Sha256,kind=d.Kind,template=d.Kind=="aluminum"?"cut-v1":"rectangular-glass-v1",
            issues=d.Kind=="glass"?new[]{new {code="MATERIAL_UNVERIFIED",severity="warning"}}:[],
            rows=input.Ops.Select((op,i)=>new{op,quantity=2,documentSha256=d.Sha256,source=new{page=1,line=i+1,bbox=new[]{10,20,30,40}},
                reference="REF1",series="SERIES",description="synthetic",pieceId=op+"-001",measure="100.1",ct="90",leftAngle="45",rightAngle="90",width="200",height="300",originalDimensions="200 x 300",dimensionUnit=(string?)null}).ToArray()}).ToArray()});
    public static async Task Verify(IncrementalHttpHarness h)
    {
        await using var db=h.Db();var preparations=await db.Set<ProductionPreparation>().OrderBy(p=>p.Id).ToArrayAsync();var id=preparations[0].Id;
        var beforeRows=await db.Set<ProductionCandidateRow>().CountAsync();var tasks=await db.ProjectTasks.CountAsync();var documents=await db.ProjectDocuments.CountAsync();
        h.EnableExtraction();
        async Task<ExtractionJobView> Queue(Guid p)
        {using var response=await h.Send("operations","OperationsHead",HttpMethod.Post,$"operations/preparations/{p}/extraction");await IncrementalHttpHarness.Status(response,202);return (await response.Content.ReadFromJsonAsync<ExtractionJobView>(IncrementalHttpHarness.Json))!;}
        var submitted=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>Queue(id)));Assert.Single(submitted.Select(j=>j.Id).Distinct());
        var entered=new TaskCompletionSource<ExtractionInput>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow=new Processor(async input=>{entered.SetResult(input);await release.Task;return Report(input);});
        var config=Options.Create(new DocumentExtractionOptions{Enabled=true,TimeoutSeconds=1});
        async Task<bool> Run(IDocumentComponentProcessor processor)
        {await using var context=h.Db();return await new EfDocumentExtractionStore(context,h.Clock,config,h.Files,processor).RunOneAsync(default);}
        var old=Run(slow);await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await Run(new Processor(i=>Task.FromResult(Report(i))))); // Live lease excludes another worker.
        h.Clock.Now=h.Clock.Now.AddSeconds(62);
        Assert.True(await Run(new Processor(_=>throw new ExtractionProcessingException("ProcessorTimeout"))));
        release.SetResult();await old; // Stale worker must not overwrite the newer failed attempt.
        var failed=await h.Get<ExtractionJobView>("operations","OperationsHead",$"operations/preparations/{id}/extraction");Assert.Equal("Failed",failed.State);Assert.Equal(2,failed.Attempts);
        Assert.Equal(beforeRows,await db.Set<ProductionCandidateRow>().CountAsync());
        await Queue(id);var successful=new Processor(i=>Task.FromResult(Report(i)));
        var workers=await Task.WhenAll(Run(successful),Run(successful));Assert.Single(workers,x=>x);
        var done=await h.Get<ExtractionJobView>("operations","OperationsHead",$"operations/preparations/{id}/extraction");Assert.Equal("Completed",done.State);Assert.Equal(3,done.Attempts);
        var total=await db.Set<ProductionCandidateRow>().CountAsync();Assert.Equal(beforeRows+2*await db.Set<PreparationSourceMember>().CountAsync(m=>m.PreparationId==id),total);await Queue(id);Assert.False(await Run(successful));Assert.Equal(total,await db.Set<ProductionCandidateRow>().CountAsync());
        Assert.Equal(PreparationState.Extracted,(await db.Set<ProductionPreparation>().AsNoTracking().SingleAsync(p=>p.Id==id)).State);
        var candidates=await h.Get<CandidatePage>("operations","OperationsHead",$"operations/preparations/{id}/candidates");Assert.NotEmpty(candidates.Rows);
        Assert.All(await db.Set<ProductionCandidateRow>().Where(r=>r.PreparationId==id&&r.ExtractorVersion==DocumentExtractorVersion.Current&&r.Measure==100.1m).ToArrayAsync(),r=>Assert.Equal(90m,r.CutDimension));
        Assert.Contains(await db.Set<ProductionCandidateRow>().Where(r=>r.PreparationId==id&&r.ExtractorVersion==DocumentExtractorVersion.Current).ToArrayAsync(),r=>r.Material==null&&r.ProposedRoute==null);
        // Integrity rejection before the processor sees any bytes. Restore only this synthetic test file.
        var second=preparations[1];var source=await db.Set<PreparationSourceDocument>().FirstAsync(d=>d.PreparationId==second.Id&&d.Category=="UnoptimizedCutList");var file=await db.ProjectDocuments.SingleAsync(d=>d.Id==source.DocumentId);
        var original=await h.Files.ReadAsync(file.StorageKey,default);
        try
        {
            await h.Files.DeleteAsync(file.StorageKey,default);await h.Files.SaveAsync(file.StorageKey,new byte[original.Length],default);
            await Queue(second.Id);Assert.True(await Run(new Processor(_=>throw new Exception("Processor must not receive corrupt bytes"))));
            var blocked=await h.Get<ExtractionJobView>("operations","OperationsHead",$"operations/preparations/{second.Id}/extraction");Assert.Equal("Blocked",blocked.State);Assert.Equal("DocumentHashInvalid",blocked.ErrorCode);
            await Queue(second.Id);Assert.False(await Run(successful));
        }
        finally {await h.Files.DeleteAsync(file.StorageKey,default);await h.Files.SaveAsync(file.StorageKey,original,default);}
        Assert.Equal(tasks,await db.ProjectTasks.CountAsync());Assert.Equal(documents,await db.ProjectDocuments.CountAsync());
        await ProductionHumanReviewHttpChecks.Verify(h,id,second.Id);
    }
}
