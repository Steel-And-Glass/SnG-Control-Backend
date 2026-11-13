using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.Persistence;
internal static class OpeningFilingHttpChecks
{
    internal static async Task Verify(IncrementalHttpHarness h, byte[] pdf)
    {
        Task<OpeningFilingsView> Read() => h.Get<OpeningFilingsView>("worker", "Engineering", h.Path("opening-filings"));
        async Task<OpeningFilingView> Filing(Guid id) => (await Read()).Filings.Single(f=>f.Id==id);
        async Task Send(Guid id,OpeningFilingCommand c,int status=204,string actor="worker",string role="Engineering")
        {using var r=await h.Send(actor,role,HttpMethod.Post,h.Path($"opening-filings/{id}/commands"),c);await IncrementalHttpHarness.Status(r,status);}
        OpeningFilingCommand Command(OpeningFilingView f,string action)=>new(Guid.NewGuid(),f.Revision,action,null,null,null,null);
        async Task Upload(OpeningFilingView f,string category,Guid? previous=null,int status=204,byte[]? bytes=null,string name="technical.pdf",Guid? key=null,string? mime="application/pdf")
        {using var form=new MultipartFormDataContent();form.Add(JsonContent.Create(new OpeningFilingUpload(key??Guid.NewGuid(),f.Revision,category,previous),options:IncrementalHttpHarness.Json),"manifest");var file=new ByteArrayContent(bytes??pdf);if(mime!=null)file.Headers.ContentType=new(mime);form.Add(file,"file",name);using var r=await h.Send("worker","Engineering",HttpMethod.Post,h.Path($"opening-filings/{f.Id}/documents"),form);await IncrementalHttpHarness.Status(r,status);}
        async Task Download(Guid filing,Guid version,Guid document,int status=200,string actor="worker",string role="Engineering")
        {using var r=await h.Send(actor,role,HttpMethod.Get,h.Path($"opening-filings/{filing}/versions/{version}/documents/{document}/download"));await IncrementalHttpHarness.Status(r,status);if(status==200)Assert.Equal(pdf,await r.Content.ReadAsByteArrayAsync());}
        var all=await Read();Assert.Equal(4,all.Filings.Count);Assert.All(all.Filings,f=>{Assert.Equal("Pending",f.Status);Assert.Equal("worker",f.ResponsibleSubject);Assert.True(f.CanModify);Assert.Empty(f.Versions);});
        var first=all.Filings.First(f=>f.Structures.Count==2);var second=all.Filings.First(f=>f.Id!=first.Id);
        var save=Command(first,"Save") with{Number=" 1162001 ",Members=first.Structures.Select((s,i)=>new OpeningFilingOp(s.StructureId,$"OP{i+1}",true)).ToArray()};
        await Send(first.Id,save,403,"engineer");await Send(first.Id,save,403,"worker","Management");await Send(first.Id,save,401,"","");
        await Send(first.Id,save with{Members=[new(second.Structures[0].StructureId,"OP1",true)]},409);
        await Send(first.Id,save with{Members=first.Structures.Select(s=>new OpeningFilingOp(s.StructureId,"OP1",true)).ToArray()},409);
        await Send(first.Id,save with{Members=[new(Guid.NewGuid(),"OP1",true)]},409);
        await Task.WhenAll(Send(first.Id,save),Send(first.Id,save));first=await Filing(first.Id);Assert.Single(first.Versions);Assert.Equal("Preparing",first.Status);
        await Send(first.Id,Command(first,"Confirm"),409);Assert.Equal("Pending",(await Filing(second.Id)).Status);
        await Send(second.Id,Command(second,"Save") with{Number="1162001",Members=[]},409);
        await Upload(first,"Sections",bytes:[1,2,3],status:400);await Upload(first,"Sections",name:"danger.exe",status:400);
        var uploadKey=Guid.NewGuid();await Upload(first,"Sections",key:uploadKey);await Upload(first,"Sections",key:uploadKey);first=await Filing(first.Id);Assert.Single(first.Versions[0].Documents);
        await Upload(first,"Sections");first=await Filing(first.Id);Assert.Equal(2,first.Versions[0].Documents.Count);
        var old=first.Versions[0].Documents[0].DocumentId;var v1=first.Versions[0].Id;
        await Upload(first,"Sections",old);first=await Filing(first.Id);Assert.Equal("Replaced",first.Versions[0].Documents.Single(d=>d.DocumentId==old).State);await Download(first.Id,v1,old);
        await Download(second.Id,v1,old,404);await Download(first.Id,v1,old,403,"other-advisor","CommercialAdvisor");
        await Upload(first,"Plans",second.SignedDocumentId,409);
        var extra=first.Versions[0].Documents.First(d=>d.State=="Current").DocumentId;
        await Send(first.Id,Command(first,"Withdraw") with{DocumentId=extra});first=await Filing(first.Id);Assert.Contains(first.Versions[0].Documents,d=>d.State=="Withdrawn");await Download(first.Id,v1,extra);
        foreach(var category in OpeningFilingRules.Categories.Take(7).Where(c=>c!="Sections")){await Upload(first,category);first=await Filing(first.Id);}
        // Additional files are opaque: arbitrary extension, active MIME, no MIME and disguised PDF.
        var additions = new[] { ("script.HTML", "text/html", "<script>never execute</script>"u8.ToArray()), ("drawing.custom-format", "application/x-custom", new byte[]{0,255,1,2}), ("without-extension", (string?)null, new byte[]{77,90,3}), ("opaque.pdf", "application/pdf", new byte[]{9,8,7}) };
        foreach(var (name,mime,bytes) in additions)
        {
            await Upload(first,"Additional",bytes:bytes,name:name,mime:mime);first=await Filing(first.Id);
            var saved=first.Versions[0].Documents.Single(d=>d.Name==name);
            Assert.Equal(mime,saved.ContentType);Assert.Equal(bytes.Length,saved.SizeBytes);Assert.Equal("worker",saved.UploadedBySubject);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),saved.Sha256);
            using var response=await h.Send("worker","Engineering",HttpMethod.Get,h.Path($"opening-filings/{first.Id}/versions/{v1}/documents/{saved.DocumentId}/download"));
            await IncrementalHttpHarness.Status(response,200);Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());Assert.Equal("application/octet-stream",response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal("nosniff",Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));Assert.True(response.Headers.CacheControl!.NoStore);
            await Download(first.Id,v1,saved.DocumentId,403,"other-advisor","CommercialAdvisor");
        }
        var originalAdditional=first.Versions[0].Documents.Single(d=>d.Name=="script.HTML");
        await Upload(first,"Additional",originalAdditional.DocumentId,bytes:[7,6,5],name:"replacement.exe",mime:"application/x-msdownload");first=await Filing(first.Id);
        Assert.Equal("Replaced",first.Versions[0].Documents.Single(d=>d.DocumentId==originalAdditional.DocumentId).State);
        Assert.Equal(2,first.Versions[0].Documents.Single(d=>d.Name=="replacement.exe").Version);
        using(var historic=await h.Send("worker","Engineering",HttpMethod.Get,h.Path($"opening-filings/{first.Id}/versions/{v1}/documents/{originalAdditional.DocumentId}/download"))) {await IncrementalHttpHarness.Status(historic,200);Assert.Equal(additions[0].Item3,await historic.Content.ReadAsByteArrayAsync());}
        foreach(var category in OpeningFilingRules.Categories.Take(7)) await Upload(first,category,status:400,bytes:[1,2,3],name:"not-a-pdf.html",mime:"text/html");
        await Upload(first,"Additional",status:400,bytes:[],name:"empty.bin",mime:null);
        await Upload(first,"Additional",status:400,bytes:[1],name:"../escape.bin",mime:null);
        h.SetFilingLimits(8,2L*1024*1024*1024);
        try { await Upload(first,"Additional",status:413,bytes:new byte[9],name:"large.bin",mime:null); }
        finally { h.SetFilingLimits(25*1024*1024,2L*1024*1024*1024); }
        h.SetFilingLimits(8,8);
        try { await Upload(first,"Additional",status:413,bytes:[1],name:"quota.bin",mime:null); }
        finally { h.SetFilingLimits(25*1024*1024,2L*1024*1024*1024); }
        var confirm=Command(first,"Confirm");await Task.WhenAll(Send(first.Id,confirm),Send(first.Id,confirm));first=await Filing(first.Id);Assert.Equal("Confirmed",first.Status);Assert.NotNull(first.Versions[0].ManifestSha256);
        await Upload(first,"Additional",status:409);await Send(first.Id,Command(first,"Save") with{Number="changed",Members=save.Members},409);
        await using(var db=h.Db())
        {
            Assert.Equal(ProjectTaskStatus.Completed,(await db.ProjectTasks.SingleAsync(t=>t.Id==first.TaskId)).Status);
            Assert.Equal(ProjectTaskStatus.Pending,(await db.ProjectTasks.SingleAsync(t=>t.Id==second.TaskId)).Status);
            Assert.Equal(ProjectTaskStatus.InProgress,(await db.ProjectTasks.SingleAsync(t=>t.ProjectId==h.ProjectId&&t.Type==ProjectTaskType.PrepareOpeningRecord)).Status);
            Assert.Single(await db.Set<OpeningFilingRelease>().ToArrayAsync());
            await using var tx=await db.Database.BeginTransactionAsync();var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OpeningFilingMembers\" SET \"OpOriginal\"='OTHER' WHERE \"VersionId\"={v1}"));Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        var originalHash=first.Versions[0].ManifestSha256;
        await Send(first.Id,Command(first,"Amend") with{Reason="Replace technical drawing"});first=await Filing(first.Id);Assert.Equal(2,first.Versions.Count);Assert.Equal("Preparing",first.Status);
        await Upload(first,"Plans",first.Versions[1].Documents.Single(d=>d.Category=="Plans").DocumentId);first=await Filing(first.Id);
        await Send(first.Id,Command(first,"Confirm"));first=await Filing(first.Id);Assert.Equal(originalHash,first.Versions[0].ManifestSha256);await Download(first.Id,v1,old);
        await using(var db=h.Db()){Assert.Equal(2,await db.Set<OpeningFilingRelease>().CountAsync());Assert.Equal(4,await db.Set<OpeningFilingDelivery>().CountAsync());}
        // A second independent acceptance is unaffected and may prepare another manual number.
        second=await Filing(second.Id);await Send(second.Id,Command(second,"Save") with{Number="1162002",Members=[]});Assert.Equal("Preparing",(await Filing(second.Id)).Status);
    }
}
