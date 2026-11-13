using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class CommercialQuotationHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<CommercialQuotationScenario> Scenario()
    {
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialHead");
        fixture.Commercial?.Dispose(); fixture.Commercial = new(); await fixture.Commercial.InitializeAsync();
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "valid");
        return fixture.Commercial;
    }
    private static MultipartFormDataContent Upload(byte[] bytes, bool corrections = false)
    {
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "synthetic.xlsx");
        if (corrections) form.Add(new StringContent("{}"), "confirmation");
        return form;
    }
    [Theory]
    [InlineData("preview")] [InlineData("confirm")]
    public async Task FinancialReviewIsActionableAndCannotPersist(string action)
    {
        var scenario = await Scenario();
        var bytes = SnG.Control.Tests.Quotations.FinancialCacheTests.Flag(
            CommercialQuotationScenario.Workbook().Formula("N15", "AL15", "100").Number("AL15", 100).Build(), "force");
        using var form = Upload(bytes);
        using var response = await fixture.Http.PostAsync($"/api/projects/{scenario.State!.Project.Id}/commercial-quotation/{action}", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RecalculationRequired", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, scenario.ConfirmCalls); Assert.Null(scenario.State.Quotation);
    }
    [Theory]
    [InlineData("preview", null, 401)]
    [InlineData("confirm", null, 401)]
    [InlineData("preview", "other", 403)]
    [InlineData("confirm", "other", 403)]
    [InlineData("preview", "missing-sub", 401)]
    [InlineData("confirm", "missing-sub", 401)]
    [InlineData("preview", "head-other-email", 200)]
    [InlineData("confirm", "head-other-email", 201)]
    public async Task UploadAuthorization_PrecedesMultipartAndBusinessLogic(string action, string? identity, int expected)
    {
        var scenario = await Scenario();
        fixture.Http.DefaultRequestHeaders.Authorization = identity is null ? null : new(TestAuthenticationHandler.SchemeName, identity);
        using var form = expected < 300 ? Upload(scenario.Bytes) : new MultipartFormDataContent();
        using var response = await fixture.Http.PostAsync($"/api/projects/{scenario.State!.Project.Id}/commercial-quotation/{action}", form);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 400) { Assert.Equal(0, scenario.ConfirmCalls); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls); }
    }
    [Theory]
    [InlineData("preview")]
    [InlineData("confirm")]
    public async Task StrictMultipart_RejectsEditableCorrections_AndMissingProjectIs404(string action)
    {
        var scenario = await Scenario();
        using var invalid = Upload(scenario.Bytes, true);
        using var bad = await fixture.Http.PostAsync($"/api/projects/{scenario.State!.Project.Id}/commercial-quotation/{action}", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var valid = Upload(scenario.Bytes);
        using var missing = await fixture.Http.PostAsync($"/api/projects/{Guid.NewGuid()}/commercial-quotation/{action}", valid);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Assert.Equal(0, scenario.SaveCalls);
    }
    [Theory]
    [InlineData("valid", 200)]
    [InlineData("other", 200)]
    [InlineData("creator-other-email", 200)]
    [InlineData("outsider", 403)]
    [InlineData(null, 401)]
    public async Task FinalDownload_UsesSubject_ExactBytesAndSafeFilename(string? identity, int expected)
    {
        var scenario = await Scenario(); var id = scenario.State!.Project.Id;
        await scenario.Confirm().ExecuteAsync(id, scenario.Bytes, "folder/Cotización \"final\".xlsx", default);
        fixture.Http.DefaultRequestHeaders.Authorization = identity is null ? null : new(TestAuthenticationHandler.SchemeName, identity);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", identity is "other" or "creator-other-email" ? "CommercialAdvisor" : "CommercialHead");
        var before = JsonSerializer.Serialize(scenario.State);
        for (var index = 0; index < 2; index++)
        {
            using var response = await fixture.Http.GetAsync($"/api/projects/{id}/commercial-quotation/file?filename=ignored.xlsx");
            Assert.Equal(expected, (int)response.StatusCode);
            if (expected == 200)
            {
                Assert.Equal(scenario.Bytes, await response.Content.ReadAsByteArrayAsync());
                Assert.Equal(DownloadCommercialQuotationUseCase.ContentType, response.Content.Headers.ContentType!.MediaType);
                Assert.Equal("Cotización _final_.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar);
            }
        }
        Assert.Equal(before, JsonSerializer.Serialize(scenario.State)); Assert.Equal(1, scenario.SaveCalls); Assert.Equal(1, scenario.DeleteCalls);
    }
    [Fact]
    public async Task Confirm_ConcurrentRequestsAndRetry_OneQuotationAndTask_AndOldFileUnavailable()
    {
        var scenario = await Scenario(); var id = scenario.State!.Project.Id; var url = $"/api/projects/{id}/commercial-quotation/confirm";
        using var first = Upload(scenario.Bytes); using var second = Upload(scenario.Bytes);
        var responses = await Task.WhenAll(fixture.Http.PostAsync(url, first), fixture.Http.PostAsync(url, second));
        try
        {
            var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var json = await success.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PresentQuotationToClient", json.GetProperty("presentQuotationToClientTask").GetProperty("type").GetString());
            Assert.Equal("other-google-subject", json.GetProperty("presentQuotationToClientTask").GetProperty("assignedToGoogleSubject").GetString());
            Assert.Equal("Completed", json.GetProperty("reviewTask").GetProperty("status").GetString());
            Assert.True(json.GetProperty("hasTechnicalDifferences").GetBoolean()); Assert.Equal(123.46m, json.GetProperty("projectTotal").GetDecimal()); Assert.Equal("COP", json.GetProperty("currencyCode").GetString());
            Assert.Single(scenario.State!.Tasks, t => t.Type == ProjectTaskType.PresentQuotationToClient);
            using var retry = Upload(scenario.Bytes); using var repeated = await fixture.Http.PostAsync(url, retry);
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode); Assert.Equal(1, scenario.SaveCalls); Assert.Equal(1, scenario.DeleteCalls);
            fixture.Projects.Items.Add(scenario.State.Project); fixture.Tasks.Items.AddRange(scenario.State.Tasks);
            var tasks = await fixture.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}/tasks");
            Assert.Contains(tasks.EnumerateArray(), t => t.GetProperty("type").GetString() == "PresentQuotationToClient");
            var filtered = await fixture.Http.GetFromJsonAsync<JsonElement>("/api/project-tasks?area=Commercial&status=InProgress");
            Assert.DoesNotContain(filtered.EnumerateArray(), t => t.GetProperty("type").GetString() == "PresentQuotationToClient");
            using var advisor = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
            advisor.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "other");
            advisor.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialAdvisor");
            var advisorTasks = await advisor.GetFromJsonAsync<JsonElement>("/api/project-tasks?area=Commercial&status=InProgress");
            Assert.Contains(advisorTasks.EnumerateArray(), t => t.GetProperty("projectId").GetGuid() == id && t.GetProperty("type").GetString() == "PresentQuotationToClient");
            var state = scenario.State;
            fixture.Handoff = new() { State = new(state.Project, state.Tasks, state.Version, state.Catalog, state.Items, state.Extractions) };
            using var old = await fixture.Http.GetAsync($"/api/projects/{id}/budget-quotation/commercial-file");
            Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Fact]
    public async Task Preview_ExposesReviewOnlyContractWithoutInternalPaths()
    {
        var scenario = await Scenario(); using var form = Upload(scenario.Bytes);
        using var response = await fixture.Http.PostAsync($"/api/projects/{scenario.State!.Project.Id}/commercial-quotation/preview", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("technicalDifferencesCount").GetInt32());
        var item = json.RootElement.GetProperty("items")[0]; Assert.Equal(7.13m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Glass", item.GetProperty("technicalDifferences")[0].GetProperty("field").GetString());
        Assert.DoesNotContain("StorageKey", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scenario.Directory.Root, body); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
    }
}
