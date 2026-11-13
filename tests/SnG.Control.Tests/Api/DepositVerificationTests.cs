using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Api;

public sealed class DepositVerificationTests
{
    [Theory]
    [InlineData("valid", "CommercialAdvisor", false, 200)]
    [InlineData("other", "CommercialAdvisor", false, 403)]
    [InlineData("accounting", "Accounting", false, 403)]
    [InlineData("management", "Management", false, 403)]
    [InlineData("accounting", "Accounting", true, 204)]
    [InlineData("valid", "CommercialAdvisor", true, 403)]
    [InlineData("valid", "CommercialHead", true, 403)]
    [InlineData("management", "Management", true, 403)]
    [InlineData("budgets", "Budgets", true, 403)]
    [InlineData("other", "Accounting", true, 403)]
    public async Task WritesRequireOwnedActiveRoleAndAssignedIdentity(string identity, string role, bool decide, int expected)
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, identity, role);
        var path = $"/api/projects/{store.Id}/deposit-verifications" + (decide ? $"/{store.ReviewId}/decision" : "");
        object body = decide ? new { approve = true, amount = "5000000.00", receivedAt = "2026-01-01T12:00:00-05:00", note = (string?)null }
            : new { requestKey = Guid.NewGuid(), documentIds = new[] { Guid.NewGuid(), Guid.NewGuid() } };
        using var result = await fixture.Http.PostAsJsonAsync(path, body); Assert.Equal(expected, (int)result.StatusCode);
        Assert.Equal(expected < 300 ? 1 : 0, store.Writes);
    }
    [Theory]
    [InlineData("0")][InlineData("-1")][InlineData("1000000000000000000.00")][InlineData("0.001")][InlineData("1e3")][InlineData("1,50")]
    public async Task InvalidMoneyFailsBeforeStore(string amount) => await InvalidDecision(new { approve = true, amount, receivedAt = "2026-01-01T12:00:00Z", note = (string?)null });
    [Theory]
    [InlineData("2026-01-01T12:00:00")][InlineData("2099-01-01T12:00:00Z")][InlineData("invalid")]
    public async Task InvalidReceiptFailsBeforeStore(string receivedAt) => await InvalidDecision(new { approve = true, amount = "1.00", receivedAt, note = (string?)null });
    [Theory]
    [InlineData(null)][InlineData("")][InlineData(" ")]
    public async Task RejectionRequiresObservation(string? note) => await InvalidDecision(new { approve = false, amount = (string?)null, receivedAt = (string?)null, note });
    [Theory]
    [InlineData("recordedAt")][InlineData("subject")][InlineData("currencyCode")]
    public async Task RejectsCallerAuthority(string field) => await InvalidDecision(new Dictionary<string, object?> { ["approve"] = true, ["amount"] = "1.00", ["receivedAt"] = "2026-01-01T12:00:00Z", ["note"] = null, [field] = "forbidden" });
    [Fact]
    public async Task MoneyUsesExistingNormalizationAndAwareUtcReceipt()
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, "accounting", "Accounting");
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/deposit-verifications/{store.ReviewId}/decision", new { approve = true, amount = "1.005", receivedAt = "2026-01-01T12:00:00-05:00", note = (string?)null });
        Assert.Equal(204, (int)result.StatusCode); Assert.Equal(1.01m, store.Input!.Amount); Assert.Equal(17, store.Input.ReceivedAt.Hour); Assert.Equal(DateTimeKind.Utc, store.Input.ReceivedAt.Kind);
        Assert.Equal("installation-test-subject", store.Installer);
    }
    [Fact]
    public async Task MissingInstallationConfigurationBlocksApprovalWithoutWrites()
    {
        var store = new Store(); await using var fixture = Fixture(store, ""); await fixture.InitializeAsync(); Identify(fixture, "accounting", "Accounting");
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/deposit-verifications/{store.ReviewId}/decision", new { approve = true, amount = "1.00", receivedAt = "2026-01-01T12:00:00Z", note = (string?)null });
        Assert.Equal(503, (int)result.StatusCode); Assert.Equal(0, store.Writes);
        var payload = await result.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("InstallationAssigneeNotConfigured", payload.GetProperty("code").GetString());
        using var read = await fixture.Http.GetAsync($"/api/projects/{store.Id}/deposit-verifications");
        Assert.Equal(200, (int)read.StatusCode); Assert.Equal(0, store.Writes);
    }
    [Fact]
    public async Task RejectionAcceptsObservationWithoutMonetaryFields()
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, "accounting", "Accounting");
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/deposit-verifications/{store.ReviewId}/decision", new { approve = false, amount = (string?)null, receivedAt = (string?)null, note = "Verificar soporte" });
        Assert.Equal(204, (int)result.StatusCode); Assert.Null(store.Input); Assert.Equal(1, store.Writes);
    }
    [Fact]
    public async Task EmptyAndDuplicateDocumentSelectionsAreRejected()
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); var same = Guid.NewGuid();
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { same, same }, new[] { Guid.Empty } })
        {
            using var response = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/deposit-verifications", new { requestKey = Guid.NewGuid(), documentIds = ids });
            Assert.Equal(400, (int)response.StatusCode);
        }
        Assert.Equal(0, store.Writes);
    }
    private static async Task InvalidDecision(object body)
    {
        var store = new Store(); await using var fixture = Fixture(store); await fixture.InitializeAsync(); Identify(fixture, "accounting", "Accounting");
        using var result = await fixture.Http.PostAsJsonAsync($"/api/projects/{store.Id}/deposit-verifications/{store.ReviewId}/decision", body);
        Assert.Equal(400, (int)result.StatusCode); Assert.Equal(0, store.Writes);
    }
    private static ApiFixture Fixture(Store store, string installer = "installation-test-subject") => new() { ConfigureServices = s => {
        s.RemoveAll<IDepositVerificationStore>(); s.AddSingleton<IDepositVerificationStore>(store);
        s.Configure<InstallationAssigneeOptions>(options => options.InstallationAssigneeSubject = installer);
    } };
    private static void Identify(ApiFixture fixture, string identity, string role)
    {
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, identity);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role"); fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
    }
    private sealed class Store : IDepositVerificationStore
    {
        public Guid Id { get; } = Guid.NewGuid(); public Guid ReviewId { get; } = Guid.NewGuid(); public int Writes { get; private set; }
        public DepositInput? Input { get; private set; } public string? Installer { get; private set; }
        public Task<VerificationState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token) => Task.FromResult<VerificationState?>(new(id, true, false, false, true, AccountingState.From(id, null, null), [], []));
        public Task<Guid> SubmitAsync(Guid id, VerificationSubmission input, DecisionActor actor, string accountant, CancellationToken token)
        { if (actor.Subject != "google-test-subject") throw new DocumentException(403, "VerificationAccessDenied"); Writes++; return Task.FromResult(ReviewId); }
        public Task DecideAsync(Guid id, Guid reviewId, bool approve, DepositInput? input, string? note, DecisionActor actor, string installer, CancellationToken token)
        { Writes++; Input = input; Installer = installer; return Task.CompletedTask; }
    }
}
