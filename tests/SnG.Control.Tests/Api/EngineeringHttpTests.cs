using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
namespace SnG.Control.Tests.Api;
public sealed class EngineeringFixture : ApiFixture
{
    public EngineeringDouble Store { get; } = new();
    public EngineeringFixture() => ConfigureServices = services =>
    {
        services.Configure<EngineeringAssigneeOptions>(o => o.EngineeringAssigneeSubject = "google-test-subject");
        services.RemoveAll<IEngineeringStore>(); services.AddSingleton<IEngineeringStore>(Store);
    };
}
public sealed class EngineeringHttpTests(EngineeringFixture fixture) : IClassFixture<EngineeringFixture>
{
    [Theory]
    [InlineData("Engineering", "valid", 204)]
    [InlineData("Management", "valid", 403)]
    [InlineData("CommercialAdvisor", "valid", 403)]
    [InlineData("Engineering", "other", 403)]
    [InlineData(null, "valid", 403)]
    public async Task ChecksRequireOwnedActiveEngineeringRole(string? role, string identity, int status)
    {
        using var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, identity);
        if(role != null) client.DefaultRequestHeaders.Add(HttpActiveRoleContext.HeaderName, role);
        using var response = await client.PutAsJsonAsync($"/api/projects/{Guid.NewGuid()}/engineering/structures/{Guid.NewGuid()}/check", new EngineeringCheck(Guid.NewGuid(), 0, true));
        Assert.Equal(status, (int)response.StatusCode);
    }
    [Fact]
    public async Task InvalidCommandsAreRejectedWithoutCallingPersistence()
    {
        using var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "valid");
        client.DefaultRequestHeaders.Add(HttpActiveRoleContext.HeaderName, "Engineering");
        var calls = fixture.Store.Calls;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/projects/{Guid.NewGuid()}/engineering/structures/{Guid.NewGuid()}/check", new EngineeringCheck(Guid.Empty, -1, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/engineering/complete", new EngineeringClose(Guid.NewGuid(), 0, Guid.Empty))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/engineering/initialize-historical", new EngineeringHistorical(Guid.NewGuid(), ""))).StatusCode);
        Assert.Equal(calls, fixture.Store.Calls);
    }
}
public sealed class EngineeringDouble : IEngineeringStore
{
    public int Calls;
    public Task<EngineeringState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token) => Task.FromResult<EngineeringState?>(null);
    public Task CheckAsync(Guid id, Guid structureId, EngineeringCheck command, DecisionActor actor, CancellationToken token) { Calls++; return Task.CompletedTask; }
    public Task CloseAsync(Guid id, EngineeringClose command, DecisionActor actor, CancellationToken token) { Calls++; return Task.CompletedTask; }
    public Task UploadAsync(Guid id, Guid key, long revision, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token) => throw new NotSupportedException();
    public Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, ProjectReadScope scope, CancellationToken token) => throw new NotSupportedException();
    public Task<byte[]?> SketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token) => Task.FromResult<byte[]?>(null);
    public Task InitializeHistoricalAsync(Guid id, EngineeringHistorical command, DecisionActor actor, CancellationToken token) { Calls++; return Task.CompletedTask; }
}
