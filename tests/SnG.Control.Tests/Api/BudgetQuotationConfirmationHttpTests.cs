using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class BudgetQuotationConfirmationHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Confirmation_ReturnsOfficialAndExtractedValuesWithoutInternalFilesAndDoesNotChangeWorkflow()
    {
        var (scenario, request) = await Setup();
        request["items"]![0]!["width"] = 2.54m; request["items"]![0]!["glass"] = "Corrected glass"; request["items"]![0]!["quantity"] = 8;
        var before = JsonSerializer.Serialize(new { scenario.Project, scenario.Task });
        using var response = await Post(scenario, request.ToJsonString());
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.Null(response.Headers.Location);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, result.GetProperty("quotationVersionId").GetGuid()); Assert.NotEqual(Guid.Empty, result.GetProperty("commercialCatalogId").GetGuid());
        Assert.Equal(1, result.GetProperty("versionNumber").GetInt32()); Assert.Equal(2, result.GetProperty("itemCount").GetInt32());
        Assert.Equal(10, result.GetProperty("totalQuantity").GetInt64());
        Assert.Equal(1, result.GetProperty("items")[0].GetProperty("extracted").GetProperty("width").GetDecimal());
        Assert.Equal(2.54m, result.GetProperty("items")[0].GetProperty("confirmed").GetProperty("width").GetDecimal());
        Assert.Equal("Stored", result.GetProperty("items")[0].GetProperty("image").GetProperty("status").GetString());
        Assert.Equal("Additional", result.GetProperty("items")[1].GetProperty("section").GetString());
        Assert.Equal("InProgress", result.GetProperty("prepareQuotationTask").GetProperty("status").GetString());
        Assert.Equal(scenario.Task.StartedAt, result.GetProperty("prepareQuotationTask").GetProperty("startedAt").GetDateTime());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("prepareQuotationTask").GetProperty("completedAt").ValueKind);
        Assert.Equal(before, JsonSerializer.Serialize(new { scenario.Project, scenario.Task }));
        foreach (var excluded in new[] { "storageKey", "rootPath", "base64" }) Assert.DoesNotContain(excluded, result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", result.GetProperty("items").GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InvalidEconomics", result.GetProperty("economics").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("economics").GetProperty("projectTotal").ValueKind);
        using var repeat = await Post(scenario, request.ToJsonString()); Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-confirmation")]
    [InlineData("duplicate-file")]
    [InlineData("duplicate-confirmation")]
    [InlineData("extra")]
    [InlineData("wrong-json-type")]
    [InlineData("wrong-charset")]
    [InlineData("invalid-json")]
    [InlineData("null-json")]
    [InlineData("json-path")]
    public async Task InvalidMultipart_Is400AndDoesNotPersist(string kind)
    {
        var (scenario, request) = await Setup();
        using var form = new MultipartFormDataContent();
        if (kind != "missing-file") form.Add(new ByteArrayContent(scenario.Bytes), "file", "synthetic.xlsx");
        if (kind != "missing-confirmation")
        {
            var json = new StringContent(kind == "invalid-json" ? "{" : kind == "null-json" ? "null" : request.ToJsonString(), Encoding.UTF8, kind == "wrong-json-type" ? "text/plain" : "application/json");
            if (kind == "wrong-charset") json.Headers.ContentType!.CharSet = "utf-16";
            form.Add(json, "confirmation");
        }
        if (kind == "duplicate-file") form.Add(new ByteArrayContent(scenario.Bytes), "file", "second.xlsx");
        if (kind == "duplicate-confirmation") form.Add(new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"), "confirmation");
        if (kind == "extra") form.Add(new StringContent("extra"), "other");
        using var pathBody = new StringContent("{\"path\":\"https://example.invalid/file.xlsx\"}", Encoding.UTF8, "application/json");
        using var response = await fixture.BudgetsHttp.PostAsync(Url(scenario), kind == "json-path" ? pathBody : form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.False(fixture.Confirmations.Confirmations.ContainsKey(scenario.Project.Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("row")]
    [InlineData("duplicate")]
    [InlineData("section")]
    [InlineData("number")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("area")]
    [InlineData("quantity")]
    [InlineData("fraction")]
    [InlineData("image")]
    [InlineData("metadata")]
    [InlineData("price")]
    [InlineData("unknown")]
    [InlineData("numeric-section")]
    [InlineData("combined-section")]
    public async Task InvalidConfirmation_Is400WithoutPersistence(string kind)
    {
        var (scenario, request) = await Setup(); var items = request["items"]!.AsArray(); var first = items[0]!;
        switch (kind)
        {
            case "missing": items.RemoveAt(1); break;
            case "extra": items.Add(first.DeepClone()); break;
            case "row": first["sourceRow"] = 99; break;
            case "duplicate": items[1] = first.DeepClone(); break;
            case "section": first["section"] = "Additional"; break;
            case "number": first["itemNumber"] = " "; break;
            case "width": first["width"] = 0; break;
            case "height": first["height"] = -1; break;
            case "area": first["squareMeters"] = 0; break;
            case "quantity": first["quantity"] = 0; break;
            case "fraction": first["quantity"] = 1.5m; break;
            case "image": first["image"] = new JsonObject(); break;
            case "metadata": request["document"] = new JsonObject(); break;
            case "price": first["unitPrice"] = 100; break;
            case "unknown": request["other"] = "value"; break;
            case "numeric-section": first["section"] = 1; break;
            case "combined-section": first["section"] = "Main,Main"; break;
        }
        using var response = await Post(scenario, request.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.False(fixture.Confirmations.Confirmations.ContainsKey(scenario.Project.Id));
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("Npgsql", body); Assert.DoesNotContain("RootPath", body); Assert.DoesNotContain("OpenXml", body);
    }

    [Theory]
    [InlineData("missing", 404)]
    [InlineData("not-sent", 409)]
    [InlineData("mismatch", 409)]
    [InlineData("invalid-xlsx", 400)]
    public async Task WorkflowAndWorkbookStatuses_ArePreserved(string kind, int expected)
    {
        var (scenario, request) = await Setup();
        if (kind == "missing") fixture.Projects.Items.Remove(scenario.Project);
        if (kind == "not-sent") fixture.Tasks.Items.Remove(scenario.Task);
        if (kind == "mismatch") scenario.Project.SetSgCode("SG999");
        if (kind == "invalid-xlsx") scenario.Bytes = [1, 2, 3];
        using var response = await Post(scenario, request.ToJsonString()); Assert.Equal(expected, (int)response.StatusCode);
    }

    private async Task<(ConfirmationScenario, JsonObject)> Setup()
    {
        var scenario = new ConfirmationScenario(); fixture.Projects.Items.Add(scenario.Project); fixture.Tasks.Items.Add(scenario.Task);
        return (scenario, JsonSerializer.SerializeToNode(await scenario.RequestAsync(), Json)!.AsObject());
    }

    [Fact]
    public async Task JsonPart_WithBlobFilename_IsStillReadAsConfirmationContent()
    {
        var (scenario, request) = await Setup();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(scenario.Bytes), "file", "synthetic.xlsx");
        form.Add(new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"), "confirmation", "confirmation.json");
        using var response = await fixture.BudgetsHttp.PostAsync(Url(scenario), form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
    private static string Url(ConfirmationScenario scenario) => $"/api/projects/{scenario.Project.Id}/budget-quotation/confirm";
    private async Task<HttpResponseMessage> Post(ConfirmationScenario scenario, string json)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "confirmation");
        form.Add(new ByteArrayContent(scenario.Bytes), "file", "synthetic.xlsx");
        return await fixture.BudgetsHttp.PostAsync(Url(scenario), form);
    }
}
