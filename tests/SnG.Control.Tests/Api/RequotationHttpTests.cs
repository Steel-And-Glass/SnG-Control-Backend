using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Features.Requotations;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public sealed class RequotationHttpTests
{
    [Theory]
    [InlineData(null, "CommercialAdvisor", 401)]
    [InlineData("other", "CommercialAdvisor", 201)]
    [InlineData("valid", "CommercialAdvisor", 403)]
    [InlineData("valid", "CommercialHead", 403)]
    [InlineData("budgets", "Budgets", 403)]
    [InlineData("management", "Management", 403)]
    public async Task OnlyAssignedActiveAdvisorCanRequest(string? identity, string role, int expected)
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync();
        Identify(fixture, identity, role);
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/requotation-request", new { requestedChanges = "  Formal changes  " });
        Assert.Equal(expected, (int)result.StatusCode); Assert.Equal(expected == 201, store.Saved);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requestedChanges\":\" \"}")]
    [InlineData("{\"requestedChanges\":null}")]
    [InlineData("{\"requestedChanges\":\"valid\",\"targetQuotationRound\":2}")]
    [InlineData("{\"requestedChanges\":\"valid\",\"subject\":\"other\"}")]
    public async Task ClosedRequestRejectsCallerState(string body)
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, "other", "CommercialAdvisor");
        using var result = await fixture.Http.PostAsync($"/api/projects/{store.Id}/requotation-request", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(400, (int)result.StatusCode); Assert.False(store.Saved);
    }
    [Theory]
    [InlineData(RequotationStatus.AlreadyRequested)]
    [InlineData(RequotationStatus.ClientDecisionRequired)]
    [InlineData(RequotationStatus.DecisionDoesNotRequestChanges)]
    [InlineData(RequotationStatus.InvalidWorkflowState)]
    public async Task ConflictsKeepSpecificSafeCodes(RequotationStatus status)
    {
        var store = new Store { Result = status }; await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, "other", "CommercialAdvisor");
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/requotation-request", new { requestedChanges = "Formal changes" });
        Assert.Equal(409, (int)result.StatusCode); Assert.Contains(status.ToString(), await result.Content.ReadAsStringAsync());
    }
    private static ApiFixture Fixture(Store store) => new() { ConfigureServices = services => { services.RemoveAll<IRequotationStore>(); services.AddSingleton<IRequotationStore>(store); } };
    private static void Identify(ApiFixture fixture, string? identity, string role)
    {
        fixture.Http.DefaultRequestHeaders.Authorization = identity is null ? null : new(TestAuthenticationHandler.SchemeName, identity);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role"); fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
    }
    private sealed class Store : IRequotationStore
    {
        public Guid Id { get; } = Guid.NewGuid(); public bool Saved { get; private set; }
        public RequotationStatus Result { get; init; } = RequotationStatus.Success;
        public Task<QuotationRoundsResponse?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token) => Task.FromResult<QuotationRoundsResponse?>(new(id, 0, 0, false, []));
        public Task<RequotationResult> RequestAsync(Guid id, string changes, DecisionActor actor, string budgets, CancellationToken token)
        {
            if (actor.Subject != "other-google-subject") return Task.FromResult(new RequotationResult(RequotationStatus.Forbidden));
            if (Result != RequotationStatus.Success) return Task.FromResult(new RequotationResult(Result));
            Assert.Equal("Formal changes", changes); Saved = true;
            var now = TestTiming.Value.UtcNow;
            var task = new ProjectTask(id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, budgets, now, 2); TestTiming.Value.Start(task, now);
            return Task.FromResult(new RequotationResult(Result, new(new(id, Guid.NewGuid(), 2, changes, now), ProjectTaskResponse.FromEntity(task, TestTiming.Value))));
        }
    }
}
