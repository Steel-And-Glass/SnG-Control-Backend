using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Api;

public sealed class AccountingTests
{
    [Theory]
    [InlineData("valid", "CommercialAdvisor", "send-approved-quotation-to-accounting", 409)]
    [InlineData("accounting", "Accounting", "initial-deposit", 409)]
    [InlineData("accounting", "Accounting", "send-approved-quotation-to-accounting", 403)]
    [InlineData("valid", "CommercialAdvisor", "initial-deposit", 403)]
    [InlineData("management", "Management", "initial-deposit", 403)]
    [InlineData("management", "Management", "send-approved-quotation-to-accounting", 403)]
    public async Task LegacyWritesCannotBypassReceiptVerification(string identity, string role, string path, int expected)
    {
        await using var fixture = new ApiFixture(); await fixture.InitializeAsync();
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, identity);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role"); fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
        using var response = await fixture.Http.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/{path}",
            path == "initial-deposit" ? new { amount = "1.00", receivedAt = "2026-01-01T12:00:00Z", reference = (string?)null, note = (string?)null } : (object)new { });
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 409) Assert.Equal("DepositVerificationRequired", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(fixture.Tasks.Items); Assert.Empty(fixture.Tasks.Notifications);
    }
    [Fact]
    public void UnscheduledTaskHasNoInventedSlaAndPreservesQuotationTiming()
    {
        var now = TestTiming.Value.UtcNow;
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now);
        Assert.Null(task.QuotationRound); Assert.Equal(SnG.Control.Application.Scheduling.LeadTimeStatus.NotStarted, TestTiming.Value.Status(task, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestTiming.Value.Start(task, now));
        task.StartUntracked(now); task.Complete(now);
        Assert.Equal(task.StartedAt, task.CompletedAt); Assert.Null(task.DeadlineAt); Assert.Null(task.WasOverdue);
        Assert.Equal(SnG.Control.Application.Scheduling.LeadTimeStatus.UntrackedLegacy, TestTiming.Value.Status(task, now));
        Assert.Throws<InvalidOperationException>(() => task.StartUntracked(now));
        Assert.Throws<InvalidOperationException>(() => new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation).StartUntracked(now));
    }
}
