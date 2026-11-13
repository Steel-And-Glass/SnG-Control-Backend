using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Application.Features.Budgets.Status;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class BudgetQuotationStatusApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task MissingProject_Is404()
    {
        using var response = await fixture.BudgetsHttp.GetAsync($"/api/projects/{Guid.NewGuid()}/budget-quotation/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OnlyBudgetsIdentityMayRead_Anonymous401_WrongSubject403()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync();
        var url = Url();
        using var wrong = await fixture.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        using var anonymous = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        using var denied = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData("none", "NotConfirmed", false, "None")]
    [InlineData("ready", "Confirmed", true, "None")]
    [InlineData("sg", "Confirmed", false, "SgChangedAfterConfirmation")]
    [InlineData("invalid", "Confirmed", false, "InvalidState")]
    [InlineData("sent", "SentToCommercial", false, "None")]
    [InlineData("later", "SentToCommercial", false, "None")]
    public async Task OfficialStatus_IsClosedStringContract_WithoutStorage(string state, string status, bool canSend, string reason)
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync();
        var source = fixture.Handoff.State!;
        if (state == "none") fixture.Handoff.State = source with { Version = null, Catalog = null, Items = [], Extractions = [] };
        if (state == "sg") source.Project.SetSgCode("SG987");
        if (state == "invalid") fixture.Handoff.State = source with { Tasks = [new SnG.Control.Domain.Entities.ProjectTask(source.Project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject")] };
        if (state is "sent" or "later")
        {
            using var sent = await fixture.BudgetsHttp.PostAsync($"/api/projects/{source.Project.Id}/send-to-commercial", null);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            if (state == "later")
            {
                fixture.Handoff.State!.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation).Complete();
                source.Version!.ClearConfirmedFileStorageKey();
            }
        }
        fixture.Handoff.Files.Clear(); // GET still works without a single workbook/image in storage.
        using var response = await fixture.BudgetsHttp.GetAsync(Url());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(11, json.EnumerateObject().Count());
        Assert.Equal(status, json.GetProperty("status").GetString());
        Assert.Equal(reason, json.GetProperty("sendBlockReason").GetString());
        Assert.Equal(canSend, json.GetProperty("canSendToCommercial").GetBoolean());
        Assert.Equal(source.Project.Id, json.GetProperty("projectId").GetGuid());
        Assert.Equal(source.Project.SgCode, json.GetProperty("currentProjectSgCode").GetString());
        if (state == "none")
        {
            Assert.Equal(JsonValueKind.Null, json.GetProperty("versionNumber").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("confirmedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("documentSgCode").ValueKind);
            Assert.Equal(0, json.GetProperty("itemCount").GetInt32());
            Assert.Equal(0, json.GetProperty("totalQuantity").GetInt64());
        }
        else
        {
            Assert.Equal(1, json.GetProperty("versionNumber").GetInt32());
            Assert.Equal(source.Version!.ConfirmedAt, json.GetProperty("confirmedAt").GetDateTime());
            Assert.Equal(source.Items.Count, json.GetProperty("itemCount").GetInt32());
            Assert.Equal(source.Items.Sum(i => (long)i.Quantity), json.GetProperty("totalQuantity").GetInt64());
            Assert.Equal(source.Version.DocumentSgCode, json.GetProperty("documentSgCode").GetString());
        }
        foreach (var forbidden in new[] { "StorageKey", "base64", "extracted", "image", "Subject", "original.xlsx", "confirmed.xlsx" })
            Assert.DoesNotContain(forbidden, json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumNames_AreStable()
    {
        Assert.Equal(new[] { "NotConfirmed", "Confirmed", "SentToCommercial" }, Enum.GetNames<BudgetQuotationStatus>());
        Assert.Equal(new[] { "None", "SgChangedAfterConfirmation", "InvalidState" }, Enum.GetNames<BudgetQuotationSendBlockReason>());
    }

    private string Url() => $"/api/projects/{fixture.Handoff.State!.Project.Id}/budget-quotation/status";
}