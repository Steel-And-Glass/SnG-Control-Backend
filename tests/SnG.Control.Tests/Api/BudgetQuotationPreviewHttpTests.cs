using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class BudgetQuotationPreviewHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task MultipartPreview_ReturnsCompleteTechnicalContractWithoutWriting()
    {
        var project = ReadyProject();
        var task = fixture.Tasks.Items.Single(task => task.ProjectId == project.Id);
        var before = JsonSerializer.Serialize(new { project, task });
        var count = fixture.Tasks.Items.Count;
        using var form = Upload(new QuotationWorkbook().Item().Item(23, "01", additional: true).Build());
        using var response = await fixture.BudgetsHttp.PostAsync(Url(project.Id), form);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(project.Id, result.GetProperty("projectId").GetGuid()); Assert.Equal("SG0012", result.GetProperty("projectSgCode").GetString());
        Assert.Equal("Cliente sintético", result.GetProperty("document").GetProperty("clientName").GetString());
        Assert.Equal(2, result.GetProperty("itemCount").GetInt32()); Assert.Equal(4, result.GetProperty("totalQuantity").GetInt64());
        Assert.Equal("Main", result.GetProperty("items")[0].GetProperty("section").GetString());
        Assert.Equal("Additional", result.GetProperty("items")[1].GetProperty("section").GetString());
        Assert.Equal(15, result.GetProperty("items")[0].GetProperty("sourceRow").GetInt32());
        Assert.Equal("image/png", result.GetProperty("items")[0].GetProperty("image").GetProperty("contentType").GetString());
        Assert.Equal(QuotationWorkbook.FirstImage, result.GetProperty("items")[0].GetProperty("image").GetProperty("base64").GetBytesFromBase64());
        Assert.Empty(result.GetProperty("warnings").EnumerateArray());
        Assert.DoesNotContain("price", result.GetProperty("items").GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InvalidEconomics", result.GetProperty("economics").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("economics").GetProperty("projectTotal").ValueKind);
        Assert.Equal(before, JsonSerializer.Serialize(new { project, task })); Assert.Equal(count, fixture.Tasks.Items.Count);
    }

    [Fact]
    public async Task Warnings_AreStructuredAndImageIsNull()
    {
        using var form = Upload(new QuotationWorkbook().Item(image: false).Build());
        using var response = await fixture.BudgetsHttp.PostAsync(Url(ReadyProject().Id), form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingImage", json.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Equal(15, json.GetProperty("warnings")[0].GetProperty("sourceRow").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("items")[0].GetProperty("image").ValueKind);
    }

    [Theory]
    [InlineData("missing", 404)]
    [InlineData("pending", 409)]
    [InlineData("not-sent", 409)]
    [InlineData("sg-mismatch", 409)]
    public async Task InvalidProjectOrWorkflow_ReturnsExpectedStatus(string scenario, int status)
    {
        var project = ReadyProject();
        if (scenario == "not-sent") fixture.Tasks.Items.RemoveAll(task => task.ProjectId == project.Id);
        if (scenario == "pending") { fixture.Tasks.Items.RemoveAll(task => task.ProjectId == project.Id); fixture.Tasks.Items.Add(new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation)); }
        using var form = Upload(new QuotationWorkbook(scenario == "sg-mismatch" ? "SG800" : "SG0012").Item().Build());
        using var response = await fixture.BudgetsHttp.PostAsync(Url(scenario == "missing" ? Guid.NewGuid() : project.Id), form);
        Assert.Equal(status, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("wrong-field")]
    [InlineData("multiple-files")]
    [InlineData("extra-field")]
    [InlineData("empty-file")]
    [InlineData("wrong-extension")]
    [InlineData("corrupt")]
    [InlineData("json-path")]
    [InlineData("bad-multipart")]
    public async Task InvalidUpload_Is400WithoutInternalDetails(string scenario)
    {
        var project = ReadyProject();
        using HttpContent content = scenario switch
        {
            "json-path" => new StringContent("{\"path\":\"C:\\\\file.xlsx\"}", Encoding.UTF8, "application/json"),
            "bad-multipart" => new StringContent("broken", Encoding.UTF8, "multipart/form-data"),
            _ => BuildInvalidForm(scenario)
        };
        using var response = await fixture.BudgetsHttp.PostAsync(Url(project.Id), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("OpenXml", body); Assert.DoesNotContain("Npgsql", body); Assert.DoesNotContain("C:\\", body);
    }

    private static MultipartFormDataContent BuildInvalidForm(string scenario)
    {
        if (scenario == "missing-file") return new MultipartFormDataContent();
        var form = Upload(scenario == "empty-file" ? [] : scenario == "corrupt" ? [1, 2, 3] : new QuotationWorkbook().Item().Build(),
            scenario == "wrong-extension" ? "file.xlsm" : "file.xlsx", scenario == "wrong-field" ? "other" : "file");
        if (scenario == "multiple-files") form.Add(new ByteArrayContent([1]), "file", "second.xlsx");
        if (scenario == "extra-field") form.Add(new StringContent("ignored"), "other");
        return form;
    }

    private Project ReadyProject()
    {
        var project = new Project(Guid.NewGuid(), "Preview test", "City", ProductLine.Classic, "google-test-subject"); project.SetSgCode("SG0012");
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"); task.Start();
        fixture.Projects.Items.Add(project); fixture.Tasks.Items.Add(task); return project;
    }
    private static string Url(Guid id) => $"/api/projects/{id}/budget-quotation/preview";
    private static MultipartFormDataContent Upload(byte[] bytes, string name = "synthetic.xlsx", string field = "file")
    { var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), field, name); return form; }
}
