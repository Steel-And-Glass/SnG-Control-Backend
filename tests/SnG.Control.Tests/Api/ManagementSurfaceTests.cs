using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Application.Features.Management;
namespace SnG.Control.Tests.Api;
public class ManagementSurfaceTests
{
    [Fact]
    public async Task SurfaceEndpoint_IsReadOnlyManagementAndRejectsUnknownFilters()
    {
        var fixture=new ApiFixture { ConfigureServices=s=>s.Replace(ServiceDescriptor.Singleton<IManagementSurfaceStore>(new SurfaceDouble())) };
        await fixture.InitializeAsync();
        try {
        foreach(var (identity,role,expected) in new[]{("management","Management",200),("budgets","Budgets",403),("management","Budgets",403)})
        {
            using var client=new HttpClient{BaseAddress=fixture.Http.BaseAddress};
            client.DefaultRequestHeaders.Authorization=new(TestAuthenticationHandler.SchemeName,identity);
            client.DefaultRequestHeaders.Add("X-SnG-Active-Role",role);
            Assert.Equal(expected,(int)(await client.GetAsync("api/management/analytics/surfaces")).StatusCode);
            if(expected==200)foreach(var query in new[]{"?area=5","?status=Unknown","?skip=-1","?unknown=1","?area=Engineering&area=Budgets"})
                Assert.Equal(400,(int)(await client.GetAsync("api/management/analytics/surfaces"+query)).StatusCode);
        }
        } finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public void DocumentAreaAlreadyIncludesQuantity_ReferenceIsOnePhysicalUnit()
    {
        Assert.Equal("12.3456789",SurfaceDefinition.Quotation(12.3456789m,1,0).Value);
        Assert.Equal(2.511m,SurfaceDefinition.ReferenceArea(1.35m,1.86m,false));
        Assert.Equal(0.001m,SurfaceDefinition.ReferenceArea(0.001m,1m,false));
        Assert.Null(SurfaceDefinition.ReferenceArea(null,1,false));
        Assert.Null(SurfaceDefinition.ReferenceArea(-1,1,false));
        Assert.Null(SurfaceDefinition.ReferenceArea(1,0,false));
        Assert.Null(SurfaceDefinition.ReferenceArea(1,1,true));
        Assert.Null(SurfaceDefinition.Quotation(null,0,0).Value);
    }
    private sealed class SurfaceDouble:IManagementSurfaceStore
    {
        public Task<ManagementSurfacesResponse> ReadAsync(SurfaceFilter f,CancellationToken token)=>Task.FromResult(new ManagementSurfacesResponse(DateTime.UtcNow,0,f.Skip,50,false,SurfaceDefinition.Quotation(null,0,0),SurfaceDefinition.Unavailable(0,"test","Missing"),SurfaceDefinition.Unavailable(0,"test","Missing"),[]));
    }
}
