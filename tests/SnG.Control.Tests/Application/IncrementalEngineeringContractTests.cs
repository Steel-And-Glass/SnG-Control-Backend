using Microsoft.Extensions.Options;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Application;
public sealed class IncrementalEngineeringContractTests
{
    private sealed record TestRole(SystemRole? RoleValue):IActiveRoleContext {public SystemRole? Role=>RoleValue;}
    private sealed class Store:IEngineeringOperationsStore
    {
        public int Calls;
        public Task<EngineeringCommandResult> ExecuteAsync(Guid id,EngineeringCommand command,DecisionActor actor,CancellationToken token){Calls++;return Task.FromResult(new EngineeringCommandResult(Guid.NewGuid()));}
        public Task UploadOpeningAsync(Guid id,Guid actaId,Guid key,long revision,string scopeHash,IAsyncEnumerable<DocumentUpload> uploads,DecisionActor actor,CancellationToken token)=>throw new NotSupportedException();
        public Task<DocumentDownload> DownloadOpeningAsync(Guid id,Guid actaId,Guid documentId,ProjectReadScope scope,CancellationToken token)=>throw new NotSupportedException();
    }
    private static ConfiguredEngineeringAssigneeIdentity Team()=>new(Options.Create(new EngineeringAssigneeOptions{EngineeringAssigneeSubject="coordinator",EngineeringWorkerSubjects=["worker-one","worker-two"]}));
    private static EngineeringCommand Create()=>new(Guid.NewGuid(),0,"CreateActa",null,null,[new(Guid.NewGuid(),Guid.NewGuid(),1)],null);
    [Theory]
    [InlineData(SystemRole.Management,"worker-one")]
    [InlineData(SystemRole.Engineering,"unknown")]
    [InlineData(SystemRole.CommercialAdvisor,"coordinator")]
    public async Task RoleAndConfiguredSubjectRequired(SystemRole role,string subject)
    {var store=new Store();var service=new EngineeringOperationsService(store,new IdentityDouble(subject),new TestRole(role),Team());var error=await Assert.ThrowsAsync<DocumentException>(()=>service.ExecuteAsync(Guid.NewGuid(),Create(),default));Assert.Equal(403,error.Status);Assert.Equal(0,store.Calls);}
    [Fact]
    public async Task WorkerMayCreateActaButOnlyCoordinatorAssignsPackages()
    {var store=new Store();var service=new EngineeringOperationsService(store,new IdentityDouble("worker-one"),new TestRole(SystemRole.Engineering),Team());var command=Create();await service.ExecuteAsync(Guid.NewGuid(),command,default);var error=await Assert.ThrowsAsync<DocumentException>(()=>service.ExecuteAsync(Guid.NewGuid(),command with{Action="CreatePackage",Subject="worker-two"},default));Assert.Equal(403,error.Status);Assert.Equal(1,store.Calls);}
    [Fact]
    public async Task RejectsUnknownActionsForeignShapeDuplicatesAndUnboundedSelections()
    {var store=new Store();var service=new EngineeringOperationsService(store,new IdentityDouble("coordinator"),new TestRole(SystemRole.Engineering),Team());var c=Create();foreach(var invalid in new[]{c with{Action="CommercialConfirm"},c with{Subject="unexpected"},c with{Structures=[]},c with{Structures=[c.Structures![0],c.Structures[0]]},c with{Structures=Enumerable.Range(0,501).Select(_=>new EngineeringReference(Guid.NewGuid(),Guid.NewGuid(),1)).ToArray()},c with{Structures=[c.Structures![0] with{EvidenceVersion=2}]},c with{Action="CreatePackage",Subject="not-configured"},c with{RequestKey=Guid.Empty}}){var error=await Assert.ThrowsAsync<DocumentException>(()=>service.ExecuteAsync(Guid.NewGuid(),invalid,default));Assert.Equal(400,error.Status);}Assert.Equal(0,store.Calls);}
    [Fact]
    public void ConfiguredTeamResolvesEngineeringWithoutInventedUsers()
    {var other=new IdentityDouble("other");var resolver=new ConfiguredRoleResolver(Options.Create(new CommercialAdvisorOptions()),other,other,other,other,null,Team());Assert.Contains(SystemRole.Engineering,resolver.Resolve("coordinator"));Assert.Contains(SystemRole.Engineering,resolver.Resolve("worker-two"));Assert.Empty(resolver.Resolve("WORKER-TWO"));Assert.Empty(resolver.Resolve("unknown"));}
}
