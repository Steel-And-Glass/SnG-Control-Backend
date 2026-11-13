using System.Net;
using System.Text.Json;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class CommercialHandoffApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture fixture;
    public CommercialHandoffApiTests(ApiFixture fixture)
    {
        this.fixture = fixture;
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialHead");
    }
    [Theory]
    [InlineData("send-to-commercial")]
    [InlineData("budget-quotation/commercial-file")]
    public async Task MissingProject_Is404(string route)
    {
        using var response = route == "send-to-commercial" ? await fixture.BudgetsHttp.PostAsync($"/api/projects/{Guid.NewGuid()}/{route}", null)
            : await fixture.Http.GetAsync($"/api/projects/{Guid.NewGuid()}/{route}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Theory]
    [InlineData("workflow")]
    [InlineData("confirmation")]
    [InlineData("sg")]
    public async Task NotReady_Is409(string reason)
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var state = fixture.Handoff.State!;
        if (reason == "workflow") fixture.Handoff.State = state with { Tasks = [] };
        if (reason == "confirmation") fixture.Handoff.State = state with { Version = null };
        if (reason == "sg") state.Project.SetSgCode("SG12");
        using var response = await fixture.BudgetsHttp.PostAsync($"/api/projects/{state.Project.Id}/send-to-commercial", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        if (reason == "sg") Assert.Contains("SgChangedAfterConfirmation", await response.Content.ReadAsStringAsync());
        Assert.Empty(fixture.Handoff.SavedKeys);
    }
    [Fact]
    public async Task BodyIsRejected_AndUnassignedDownloadIs404()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var id = fixture.Handoff.State!.Project.Id;
        using var post = await fixture.BudgetsHttp.PostAsync($"/api/projects/{id}/send-to-commercial", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        using var get = await fixture.Http.GetAsync($"/api/projects/{id}/budget-quotation/commercial-file");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode); Assert.Empty(fixture.Handoff.SavedKeys);
    }
    [Fact]
    public async Task Success_DuplicateAndDownloadsHaveCorrectContractAndNoSideEffects()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var state = fixture.Handoff.State!;
        // Unicode, spaces and quotes exercise ASP.NET's Content-Disposition serialization.
        typeof(SnG.Control.Domain.Entities.BudgetQuotationVersion).GetProperty("OriginalFileName")!.SetValue(state.Version, "Cotización \"operativa\".xlsx");
        var root = $"/api/projects/{state.Project.Id}";
        using var sent = await fixture.BudgetsHttp.PostAsync(root + "/send-to-commercial", null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var json = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        Assert.Equal("Completed", json.RootElement.GetProperty("budgetTask").GetProperty("status").GetString());
        Assert.Equal("InProgress", json.RootElement.GetProperty("commercialTask").GetProperty("status").GetString());
        Assert.Equal("ReviewQuotation", json.RootElement.GetProperty("commercialTask").GetProperty("type").GetString());
        Assert.True(json.RootElement.GetProperty("commercialFileAvailable").GetBoolean());
        using var duplicate = await fixture.BudgetsHttp.PostAsync(root + "/send-to-commercial", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var before = JsonSerializer.Serialize(fixture.Handoff.State);
        byte[]? previous = null;
        for (var index = 0; index < 2; index++)
        {
            using var download = await fixture.Http.GetAsync(root + "/budget-quotation/commercial-file");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(DownloadCommercialQuotationUseCase.ContentType, download.Content.Headers.ContentType!.MediaType);
            Assert.Equal("Cotización _operativa_.xlsx", download.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal("Cotización \"operativa\".xlsx", state.Version!.OriginalFileName);
            var bytes = await download.Content.ReadAsByteArrayAsync();
            Assert.Equal(fixture.Handoff.Files[state.Version.ConfirmedFileStorageKey!], bytes);
            if (previous is not null) Assert.Equal(previous, bytes); previous = bytes;
        }
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Handoff.State)); Assert.Single(fixture.Handoff.SavedKeys);
    }
    [Fact]
    public async Task MissingReferencedFile_IsGeneric500WithoutPhysicalPath()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var id = fixture.Handoff.State!.Project.Id;
        using var sent = await fixture.BudgetsHttp.PostAsync($"/api/projects/{id}/send-to-commercial", null); Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        fixture.Handoff.Files.Remove(fixture.Handoff.State!.Version!.ConfirmedFileStorageKey!);
        using var response = await fixture.Http.GetAsync($"/api/projects/{id}/budget-quotation/commercial-file");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("Synthetic", text); Assert.DoesNotContain("projects/", text);
    }
}
