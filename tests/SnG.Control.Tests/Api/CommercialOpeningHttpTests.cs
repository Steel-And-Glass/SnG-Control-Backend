using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.Api;
public sealed class CommercialOpeningFixture:ApiFixture {
 public CommercialOpeningDouble Store{get;}=new();
 public CommercialOpeningFixture()=>ConfigureServices=s=>{s.Configure<EngineeringAssigneeOptions>(o=>o.EngineeringAssigneeSubject="google-test-subject");s.RemoveAll<ICommercialOpeningStore>();s.AddSingleton<ICommercialOpeningStore>(Store);};
}
public sealed class CommercialOpeningHttpTests(CommercialOpeningFixture fixture):IClassFixture<CommercialOpeningFixture>{
 private HttpClient Client(string? role){var c=new HttpClient{BaseAddress=fixture.Http.BaseAddress};c.DefaultRequestHeaders.Authorization=new(TestAuthenticationHandler.SchemeName,"valid");if(role!=null)c.DefaultRequestHeaders.Add(HttpActiveRoleContext.HeaderName,role);return c;}
 [Theory][InlineData("CommercialAdvisor",200)][InlineData("Engineering",403)][InlineData("Management",403)][InlineData(null,403)]
 public async Task PresentationRequiresOwnedActiveAdvisor(string? role,int expected){using var c=Client(role);var command=new CommercialOpeningCommand(Guid.NewGuid(),"Present",0,Guid.NewGuid());using var r=await c.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/incremental-opening-records/{Guid.NewGuid()}/commands",command);Assert.Equal(expected,(int)r.StatusCode);}
 [Theory][InlineData("Engineering",200)][InlineData("CommercialAdvisor",403)][InlineData("Management",403)]
 public async Task ReviewRequiresActiveEngineering(string role,int expected){using var c=Client(role);var command=new CommercialOpeningCommand(Guid.NewGuid(),"ReviewCheck",0,Guid.NewGuid(),Guid.NewGuid(),[new(Guid.NewGuid(),3,1)],Completed:true);using var r=await c.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/incremental-opening-records/{Guid.NewGuid()}/commands",command);Assert.Equal(expected,(int)r.StatusCode);}
 [Fact]public async Task RejectsImplicitApprovalMissingAttestationDuplicatedSelectionsAndFreeCommands(){using var c=Client("CommercialAdvisor");var row=new CommercialOpeningSelection(Guid.NewGuid(),0,1);var valid=new CommercialOpeningCommand(Guid.NewGuid(),"Accept",0,Guid.NewGuid(),Structures:[row],DocumentId:Guid.NewGuid(),Attest:true);var before=fixture.Store.Calls;
 foreach(var bad in new[]{valid with{Attest=false},valid with{Structures=[]},valid with{Structures=[row,row]},valid with{Structures=[row with{EvidenceVersion=0}]},valid with{Action="ConfirmStructure"},valid with{Structures=Enumerable.Range(0,501).Select(_=>row with{StructureId=Guid.NewGuid()}).ToArray()},valid with{Action="RequestChanges",DocumentId=null,Attest=null}}){using var r=await c.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/incremental-opening-records/{Guid.NewGuid()}/commands",bad);Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);}Assert.Equal(before,fixture.Store.Calls);
 }
 [Fact]public async Task UploadPreservesBytesAndManifestAndRejectsUnknownMetadata(){using var c=Client("CommercialAdvisor");var path=$"/api/projects/{Guid.NewGuid()}/incremental-opening-records/{Guid.NewGuid()}/rounds/{Guid.NewGuid()}/documents";byte[] bytes="%PDF-1.4 synthetic bytes\n%%EOF"u8.ToArray();
 using var body=new MultipartFormDataContent("synthetic-boundary");body.Add(JsonContent.Create(new CommercialOpeningUpload(Guid.NewGuid(),0,"SIGNED",null,[new(Guid.NewGuid(),2,1)])),"manifest");body.Add(new ByteArrayContent(bytes),"file","signed.pdf");using var r=await c.PostAsync(path,body);Assert.Equal(HttpStatusCode.NoContent,r.StatusCode);Assert.Equal(bytes,fixture.Store.Bytes);
 var before=fixture.Store.Calls;using var bad=new MultipartFormDataContent();bad.Add(JsonContent.Create(new{requestKey=Guid.NewGuid(),expectedRevision=0,kind="SIGNED",reviewId=(Guid?)null,structures=new[]{new CommercialOpeningSelection(Guid.NewGuid(),0,1)},arbitrary="secret"}),"manifest");bad.Add(new ByteArrayContent(bytes),"file","signed.pdf");using var rejected=await c.PostAsync(path,bad);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);Assert.Equal(before,fixture.Store.Calls);
 }
}
public sealed class CommercialOpeningDouble:ICommercialOpeningStore{
 public int Calls;public byte[]? Bytes;
 public Task<CommercialOpeningState?> ReadAsync(Guid id,ProjectReadScope scope,CancellationToken token)=>Task.FromResult<CommercialOpeningState?>(null);
 public Task<CommercialOpeningResult> CommandAsync(Guid id,Guid acta,CommercialOpeningCommand command,DecisionActor actor,SystemRole role,CancellationToken token){Calls++;return Task.FromResult(new CommercialOpeningResult(Guid.NewGuid()));}
 public async Task UploadAsync(Guid id,Guid acta,Guid round,CommercialOpeningUpload command,IAsyncEnumerable<DocumentUpload> files,DecisionActor actor,SystemRole role,CancellationToken token){Calls++;await foreach(var f in files.WithCancellation(token)){using var output=new MemoryStream();await f.Content.CopyToAsync(output,token);Bytes=output.ToArray();}}
 public Task<DocumentDownload> DownloadAsync(Guid id,Guid acta,Guid doc,ProjectReadScope scope,CancellationToken token)=>throw new NotSupportedException();
}
