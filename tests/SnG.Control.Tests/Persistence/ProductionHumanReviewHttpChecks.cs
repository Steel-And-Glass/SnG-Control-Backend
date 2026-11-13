using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Domain.Entities.Production;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Persistence;
internal static class ProductionHumanReviewHttpChecks
{
    private sealed class Processor:IDocumentComponentProcessor
    {public Task<JsonElement> ProcessAsync(ExtractionInput i,CancellationToken t)=>Task.FromResult(DocumentExtractionHttpChecks.Report(i));}
    public static async Task Verify(IncrementalHttpHarness h,Guid prep,Guid blockedPrep)
    {
        await using var db=h.Db();var tasks=await db.ProjectTasks.CountAsync();var documents=await db.ProjectDocuments.CountAsync();
        var releases=await db.Set<OpeningFilingRelease>().AsNoTracking().OrderBy(r=>r.VersionId).Select(r=>r.Canonical).ToArrayAsync();
        var originals=await db.Set<ProductionCandidateRow>().AsNoTracking().OrderBy(r=>r.Id).Select(r=>r.SpecificationsJson).ToArrayAsync();
        string Path(Guid p,string suffix)=>$"operations/preparations/{p}/reviews/{suffix}";
        async Task<ReviewCommandResult> Send(Guid p,ProductionReviewCommand c,int status=200,string actor="operations",string role="OperationsHead")
        {using var response=await h.Send(actor,role,HttpMethod.Post,Path(p,"commands"),c);await IncrementalHttpHarness.Status(response,status);return status==200?(await response.Content.ReadFromJsonAsync<ReviewCommandResult>(IncrementalHttpHarness.Json))!:null!;}
        Task<ReviewView> Read(Guid p,Guid id)=>h.Get<ReviewView>("operations","OperationsHead",Path(p,id.ToString()));
        var job=await db.Set<DocumentExtractionJob>().SingleAsync(j=>j.PreparationId==prep);
        var start=new ProductionReviewCommand(Guid.NewGuid(),0,"Start","Synthetic review",ExtractionJobId:job.Id);
        await Send(prep,start,403,"management","Management");await Send(prep,start,403,"worker","Engineering");await Send(prep,start,403,"operations","Production");await Send(prep,start,401,"","");
        var current=await Send(prep,start);Assert.Equal(current,await Send(prep,start));await Send(prep,start with{Reason="different"},409);
        using(var management=await h.Send("management","Management",HttpMethod.Get,Path(prep,current.ReviewId.ToString())))await IncrementalHttpHarness.Status(management,200);
        var view=await Read(prep,current.ReviewId);Assert.NotEmpty(view.Rows);
        var row=view.Rows[0];var rowId=row.GetProperty("id").GetGuid();var document=row.GetProperty("documentId").GetGuid();
        var evidence=new ReviewEvidence(document,null,"page 1 row 1","Synthetic verified source location");
        var values=new ReviewedComponent(ComponentMaterial.AluminumProfile,FabricationRoute.Aluminum,"CHECKED","Synthetic profile",100.1m,90,null,null,"mm",45,90,2);
        ProductionReviewCommand Cmd(string action)=>new(Guid.NewGuid(),current.Revision,action,"Synthetic reason",ReviewId:current.ReviewId,Evidence:evidence);
        await Send(prep,Cmd("Approve"),409);
        await Send(prep,Cmd("Edit") with{RowId=rowId,Values=values with{Route=FabricationRoute.Glass}},400);
        await Send(prep,Cmd("Add") with{Values=values,StructureId=Guid.NewGuid(),Op="OP-FOREIGN",SourceDocumentId=document},409);
        var edit=Cmd("Edit") with{RowId=rowId,Values=values};current=await Send(prep,edit);Assert.Equal(current,await Send(prep,edit));
        var concurrent=Cmd("Edit") with{RowId=rowId,Values=values with{Description="Reviewed concurrent"}};
        var responses=await Task.WhenAll(h.Send("operations","OperationsHead",HttpMethod.Post,Path(prep,"commands"),concurrent),h.Send("operations","OperationsHead",HttpMethod.Post,Path(prep,"commands"),concurrent with{RequestKey=Guid.NewGuid()}));
        Assert.Equal(new[]{200,409},responses.Select(r=>(int)r.StatusCode).Order().ToArray());foreach(var response in responses)response.Dispose();
        view=await Read(prep,current.ReviewId);current=current with{Revision=view.Review.GetProperty("revision").GetInt64()};
        var oldReview=current.ReviewId;var beforeReextract=await db.Set<ProductionReviewRow>().AsNoTracking().Where(r=>r.ReviewId==oldReview).OrderBy(r=>r.Id).Select(r=>r.ValuesJson).ToArrayAsync();
        var reextract=Cmd("Reextract");current=await Send(prep,reextract);Assert.NotNull(current.ExtractionJobId);Assert.Equal(current,await Send(prep,reextract));
        await Send(prep,Cmd("Edit") with{RowId=rowId,Values=values},409);
        await using(var context=h.Db())Assert.True(await new EfDocumentExtractionStore(context,h.Clock,Options.Create(new DocumentExtractionOptions{Enabled=true}),h.Files,new Processor()).RunOneAsync(default));
        Assert.Equal(beforeReextract,await db.Set<ProductionReviewRow>().AsNoTracking().Where(r=>r.ReviewId==oldReview).OrderBy(r=>r.Id).Select(r=>r.ValuesJson).ToArrayAsync());
        current=await Send(prep,new(Guid.NewGuid(),0,"Start","Review new explicit extraction",ExtractionJobId:current.ExtractionJobId));
        view=await Read(prep,current.ReviewId);Assert.NotEqual(oldReview,current.ReviewId);
        foreach(var item in view.Rows)
        {
            var data=JsonSerializer.Deserialize<ReviewedComponent>(item.GetProperty("values").GetRawText(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            data=data with{Unit="mm",Description="Explicit verified specification"};
            if(data.Material==null)data=data with{Material=ComponentMaterial.MicroperforatedSheet,Route=FabricationRoute.Aluminum};
            current=await Send(prep,Cmd("Edit") with{RowId=item.GetProperty("id").GetGuid(),Values=data,Evidence=new(item.GetProperty("documentId").GetGuid(),null,"page 1","Material and dimensions explicitly reviewed")});
        }
        // Manual additions and exclusions never erase original candidates.
        var first=view.Rows[0];var add=Cmd("Add") with{Values=values,StructureId=first.GetProperty("structureId").GetGuid(),Op=first.GetProperty("op").GetString(),SourceDocumentId=first.GetProperty("documentId").GetGuid()};
        current=await Send(prep,add);view=await Read(prep,current.ReviewId);var manual=view.Rows.Single(r=>r.GetProperty("candidateId").ValueKind==JsonValueKind.Null);
        current=await Send(prep,Cmd("Exclude") with{RowId=manual.GetProperty("id").GetGuid()});
        view=await Read(prep,current.ReviewId);foreach(var issue in view.Issues)current=await Send(prep,Cmd("Resolve") with{IssueId=issue.GetProperty("id").GetGuid()});
        // Wrong bytes still block an otherwise complete, reviewed preparation.
        var file=await db.ProjectDocuments.SingleAsync(d=>d.Id==document);var original=await h.Files.ReadAsync(file.StorageKey,default);
        try{await h.Files.DeleteAsync(file.StorageKey,default);await h.Files.SaveAsync(file.StorageKey,new byte[original.Length],default);await Send(prep,Cmd("Approve"),409);}
        finally{await h.Files.DeleteAsync(file.StorageKey,default);await h.Files.SaveAsync(file.StorageKey,original,default);}
        var approve=Cmd("Approve");current=await Send(prep,approve);Assert.NotNull(current.ApprovalId);Assert.Equal(current,await Send(prep,approve));await Send(prep,Cmd("Approve"),409);await Send(prep,Cmd("Reextract"),409);
        var approved=await db.Set<ProductionApproval>().AsNoTracking().SingleAsync(a=>a.PreparationId==prep);Assert.Equal(IncrementalEvidence.Hash(approved.Canonical),approved.Sha256);
        async Task Reject(FormattableString sql)
        {await using var c=h.Db();await using var tx=await c.Database.BeginTransactionAsync();var ex=await Assert.ThrowsAsync<PostgresException>(()=>c.Database.ExecuteSqlInterpolatedAsync(sql));Assert.Equal("23514",ex.SqlState);await tx.RollbackAsync();}
        await Reject($"UPDATE \"ProductionReviewRows\" SET \"ValuesJson\"='{{}}' WHERE \"ReviewId\"={current.ReviewId}");
        await Reject($"UPDATE \"ProductionApprovals\" SET \"Sha256\"='changed' WHERE \"Id\"={approved.Id}");
        await Reject($"UPDATE \"DocumentExtractionJobs\" SET \"ReportJson\"='{{}}' WHERE \"Id\"={job.Id}");
        // Formal quantity/dimension conflicts remain open; notes and private attachments never resolve them.
        var blockedJob=await db.Set<DocumentExtractionJob>().SingleAsync(j=>j.PreparationId==blockedPrep);
        var blocked=await Send(blockedPrep,new(Guid.NewGuid(),0,"Start","Inspect blocked evidence",ExtractionJobId:blockedJob.Id));
        var evidenceKey=Guid.NewGuid();using var form=new MultipartFormDataContent();form.Add(new StringContent(evidenceKey.ToString()),"requestKey");form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("base,split\n28,35\n")),"file","discrepancy.csv");
        using var uploaded=await h.Send("operations","OperationsHead",HttpMethod.Post,Path(blockedPrep,"evidence"),form);await IncrementalHttpHarness.Status(uploaded,200);var attachment=(await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var noteEvidence=new ReviewEvidence(null,attachment,"rows 1-2","PDF 28; supporting source 35. No precedence authorized.");
        foreach(var code in new[]{"TOTAL_MISMATCH","SOURCE_DIMENSION_CONFLICT"})blocked=await Send(blockedPrep,new(Guid.NewGuid(),blocked.Revision,"RecordConflict","No formal resolution policy",ReviewId:blocked.ReviewId,ConflictCode:code,Evidence:noteEvidence));
        var blockedView=await Read(blockedPrep,blocked.ReviewId);foreach(var issue in blockedView.Issues)await Send(blockedPrep,new(Guid.NewGuid(),blocked.Revision,"Resolve","Cannot ignore conflict",ReviewId:blocked.ReviewId,IssueId:issue.GetProperty("id").GetGuid(),Evidence:noteEvidence),409);
        blocked=await Send(blockedPrep,new(Guid.NewGuid(),blocked.Revision,"Note","Retain observations only",ReviewId:blocked.ReviewId,Evidence:noteEvidence));
        await Send(blockedPrep,new(Guid.NewGuid(),blocked.Revision,"Approve","Must remain blocked",ReviewId:blocked.ReviewId),409);
        using(var download=await h.Send("management","Management",HttpMethod.Get,Path(blockedPrep,$"evidence/{attachment}"))){await IncrementalHttpHarness.Status(download,200);Assert.Equal("application/octet-stream",download.Content.Headers.ContentType!.MediaType);}
        var nextBlocked=await Send(blockedPrep,new(Guid.NewGuid(),blocked.Revision,"Reextract","Evidence restored; preserve unresolved conflicts",ReviewId:blocked.ReviewId));
        await using(var context=h.Db())Assert.True(await new EfDocumentExtractionStore(context,h.Clock,Options.Create(new DocumentExtractionOptions{Enabled=true}),h.Files,new Processor()).RunOneAsync(default));
        var newBlocked=await Send(blockedPrep,new(Guid.NewGuid(),0,"Start","Review remains blocked",ExtractionJobId:nextBlocked.ExtractionJobId));
        var inherited=await Read(blockedPrep,newBlocked.ReviewId);Assert.Contains(inherited.Issues,i=>i.GetProperty("code").GetString()=="TOTAL_MISMATCH");Assert.Contains(inherited.Issues,i=>i.GetProperty("code").GetString()=="SOURCE_DIMENSION_CONFLICT");
        await Send(blockedPrep,new(Guid.NewGuid(),newBlocked.Revision,"Approve","Cannot wash conflicts through extraction",ReviewId:newBlocked.ReviewId),409);
        var newRevisions=await db.Set<DocumentExtractionJob>().Where(j=>j.Generation==2).Select(j=>j.ComponentRevisionId).ToArrayAsync();
        Assert.Equal(originals,(await db.Set<ProductionCandidateRow>().AsNoTracking().OrderBy(r=>r.Id).ToArrayAsync()).Where(r=>!newRevisions.Contains(r.ComponentRevisionId)).Select(r=>r.SpecificationsJson));
        Assert.Equal(releases,await db.Set<OpeningFilingRelease>().AsNoTracking().OrderBy(r=>r.VersionId).Select(r=>r.Canonical).ToArrayAsync());Assert.Equal(tasks,await db.ProjectTasks.CountAsync());Assert.Equal(documents,await db.ProjectDocuments.CountAsync());
        Assert.Equal(2,await db.Set<ProductionPreparation>().CountAsync());Assert.Single(await db.Set<ProductionApproval>().ToArrayAsync());
        var rollback=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync("20261113063258_IntegrateDocumentExtraction"));Assert.Equal("23514",rollback.SqlState);
    }
}
