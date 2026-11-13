using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Api;
public sealed class OperationsFixture : ApiFixture
{
    public OperationsStoreDouble Store { get; } = new();
    public OperationsFixture() => ConfigureServices = s => {
        s.Configure<OperationsHeadOptions>(o => o.OperationsHeadSubject = "google-test-subject");
        s.RemoveAll<IDocumentExtractionStore>();s.AddSingleton<IDocumentExtractionStore,ExtractionStoreDouble>();
        s.RemoveAll<IProductionPreparationStore>(); s.AddSingleton<IProductionPreparationStore>(Store);
    };
}
public sealed class OperationsStoreDouble : IProductionPreparationStore
{
    public Task<IReadOnlyList<ReleaseSummary>> ReleasesAsync(int skip, CancellationToken t) => Task.FromResult<IReadOnlyList<ReleaseSummary>>([]);
    public Task<IReadOnlyList<PreparationSummary>> ListAsync(int skip, CancellationToken t) => Task.FromResult<IReadOnlyList<PreparationSummary>>([]);
    public Task<PreparationDetail?> ReadAsync(Guid id, CancellationToken t) => Task.FromResult<PreparationDetail?>(null);
    public Task<PreparationDetail> CreateAsync(Guid id, string actor, CancellationToken t) => throw new InvalidOperationException("No write expected in boundary tests.");
}
public class OperationsPreparationTests(OperationsFixture fixture) : IClassFixture<OperationsFixture>
{
    private HttpClient Client(string? role, string? actor = "valid")
    {
        var c = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        if (role != null) c.DefaultRequestHeaders.Add(HttpActiveRoleContext.HeaderName, role);
        if (actor != null) c.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, actor);
        return c;
    }
    [Fact] public async Task DiscoveryAndOwnedRoleRead()
    {
        using var c = Client("OperationsHead");
        var me = await c.GetFromJsonAsync<System.Text.Json.JsonElement>("api/auth/me");
        Assert.Contains(me.GetProperty("roles").EnumerateArray(), r => r.GetString() == "OperationsHead");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("api/operations/releases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("api/operations/preparations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"api/operations/preparations/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("api/operations/preparations?skip=-1")).StatusCode);
    }
    [Theory]
    [InlineData("CommercialAdvisor", "valid", 403)]
    [InlineData("CommercialHead", "valid", 403)]
    [InlineData("Management", "valid", 403)]
    [InlineData("Production", "valid", 403)]
    [InlineData("Engineering", "valid", 403)]
    [InlineData("OperationsHead", "outsider", 403)]
    [InlineData(null, "valid", 403)]
    [InlineData("OperationsHead", null, 401)]
    public async Task DeniedReadAndWrite(string? role, string? actor, int status)
    {
        using var c=Client(role,actor);
        Assert.Equal(status,(int)(await c.GetAsync("api/operations/preparations")).StatusCode);
        Assert.Equal(status,(int)(await c.GetAsync($"api/operations/preparations/{Guid.NewGuid()}/extraction")).StatusCode);
        Assert.Equal(status,(int)(await c.GetAsync($"api/operations/preparations/{Guid.NewGuid()}/candidates")).StatusCode);
        Assert.Equal(status,(int)(await c.PostAsync($"api/operations/preparations/{Guid.NewGuid()}/extraction",null)).StatusCode);
        Assert.Equal(status,(int)(await c.PostAsync($"api/operations/releases/{Guid.NewGuid()}/preparation",null)).StatusCode);
    }
    [Fact] public async Task ArbitraryStateCannotBePosted()
    {
        using var c=Client("OperationsHead");
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"api/operations/preparations/{Guid.NewGuid()}/extraction",new{rows=new[]{1}})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"api/operations/releases/{Guid.NewGuid()}/preparation",new { state="Approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.PostAsJsonAsync($"api/operations/preparations/{Guid.NewGuid()}/approve",new {})).StatusCode);
    }
}

public sealed class ExtractionStoreDouble:IDocumentExtractionStore
{
    public Task<ExtractionJobView> QueueAsync(Guid p,string a,CancellationToken t)=>throw new InvalidOperationException("Unexpected extraction");
    public Task<ExtractionJobView?> ReadAsync(Guid p,CancellationToken t)=>Task.FromResult<ExtractionJobView?>(null);
    public Task<CandidatePage> CandidatesAsync(Guid p,int skip,CancellationToken t)=>Task.FromResult(new CandidatePage(skip,[]));
}
