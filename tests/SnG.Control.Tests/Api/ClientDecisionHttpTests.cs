using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class ClientDecisionHttpTests
{
    [Theory]
    [InlineData(null, "CommercialAdvisor", 401)]
    [InlineData("other", "CommercialAdvisor", 201)]
    [InlineData("valid", "CommercialAdvisor", 403)]
    [InlineData("valid", "CommercialHead", 403)]
    [InlineData("budgets", "Budgets", 403)]
    [InlineData("management", "Management", 403)]
    public async Task RoleAndAssignedSubjectGuardWrite(string? identity, string role, int status)
    {
        var store = new Store(); var fixture = Fixture(store);
        await fixture.InitializeAsync();
        try
        {
            SetIdentity(fixture, identity, role);
            using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/client-quotation-decision", new { decision = "Approved", note = (string?)null });
            Assert.Equal(status, (int)result.StatusCode);
            Assert.Equal(status == 201, store.Recorded);
            if (status == 201)
            {
                var body = await result.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Approved", body.GetProperty("outcome").GetProperty("decision").GetString());
                Assert.Equal("Completed", body.GetProperty("task").GetProperty("status").GetString());
            }
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Theory]
    [InlineData("{\"decision\":\"Other\",\"note\":null}")]
    [InlineData("{\"decision\":\"approved\",\"note\":null}")]
    [InlineData("{\"decision\":1,\"note\":null}")]
    [InlineData("{\"decision\":\"Approved\",\"recordedAt\":\"2026-01-01\"}")]
    [InlineData("{\"note\":null}")]
    public async Task StrictRequest(string json)
    {
        var store = new Store(); var fixture = Fixture(store); await fixture.InitializeAsync();
        try
        {
            SetIdentity(fixture, "other", "CommercialAdvisor");
            using var result = await fixture.Http.PostAsync($"/api/projects/{store.Id}/client-quotation-decision", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(400, (int)result.StatusCode); Assert.False(store.Recorded);
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public async Task ManagementReadsExplicitAbsenceAndRecordedResponseWithoutIdentity()
    {
        var store = new Store(); var fixture = Fixture(store); await fixture.InitializeAsync();
        try
        {
            SetIdentity(fixture, "management", "Management");
            var path = $"/api/projects/{store.Id}/client-quotation-decision";
            var absent = await fixture.Http.GetFromJsonAsync<JsonElement>(path);
            Assert.False(absent.GetProperty("available").GetBoolean()); Assert.Equal(JsonValueKind.Null, absent.GetProperty("decision").ValueKind);
            SetIdentity(fixture, "other", "CommercialAdvisor");
            using var saved = await fixture.Http.PostAsJsonAsync(path, new { decision = "ChangesRequested", note = "Synthetic comment" });
            Assert.Equal(201, (int)saved.StatusCode);
            using var duplicate = await fixture.Http.PostAsJsonAsync(path, new { decision = "Rejected", note = (string?)null });
            Assert.Equal(409, (int)duplicate.StatusCode);
            SetIdentity(fixture, "management", "Management");
            var text = await fixture.Http.GetStringAsync(path);
            Assert.Contains("ChangesRequested", text); Assert.DoesNotContain("Subject", text); Assert.DoesNotContain("Email", text);
        }
        finally { await fixture.DisposeAsync(); }
    }
    private static ApiFixture Fixture(Store store) => new()
    {
        ConfigureServices = services =>
        {
            services.RemoveAll<IClientQuotationDecisionStore>(); services.AddSingleton<IClientQuotationDecisionStore>(store);
        }
    };
    private static void SetIdentity(ApiFixture fixture, string? identity, string role)
    {
        fixture.Http.DefaultRequestHeaders.Authorization = identity is null ? null : new(TestAuthenticationHandler.SchemeName, identity);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role"); fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
    }
    private sealed class Store : IClientQuotationDecisionStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool Recorded => value is not null;
        private ClientQuotationDecision? value;
        public Task<DecisionResult> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token) => Task.FromResult(new DecisionResult(DecisionStatus.Success, DecisionResponse.From(Id, value, 1)));
        public Task<DecisionResult> RecordAsync(Guid id, ClientQuotationDecisionType decision, string? note, DecisionActor actor, CancellationToken token)
        {
            if (actor.Subject != "other-google-subject") return Task.FromResult(new DecisionResult(DecisionStatus.Forbidden));
            if (Recorded) return Task.FromResult(new DecisionResult(DecisionStatus.AlreadyRecorded));
            var now = TestTiming.Value.UtcNow;
            var task = new ProjectTask(Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, actor.Subject, now.AddDays(-1));
            task.Start(now.AddDays(-1), now.AddDays(1)); task.Complete(now);
            value = new(Id, Guid.NewGuid(), task.Id, decision, note, now, actor.Subject, actor.Email, actor.Name);
            return Task.FromResult(new DecisionResult(DecisionStatus.Success, Recorded: new(DecisionResponse.From(Id, value, 1), ProjectTaskResponse.FromEntity(task, TestTiming.Value))));
        }
    }
}
