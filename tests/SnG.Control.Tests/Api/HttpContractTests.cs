using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SnG.Control.Tests.Api;

public class HttpContractTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Clients_CreateGetList_Return201LocationAndStoredRepresentation()
    {
        using var created = await fixture.Http.PostAsJsonAsync("/api/clients", new { name = " Cliente Demo " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cliente Demo", client.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, client.GetProperty("id").GetGuid());
        Assert.Equal(4, client.EnumerateObject().Count());
        Assert.Equal(client.GetProperty("createdAt").GetString(), client.GetProperty("updatedAt").GetString());
        Assert.EndsWith("Z", client.GetProperty("createdAt").GetString());
        using var fetched = await fixture.Http.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(client.GetRawText(), (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var listed = await fixture.Http.GetFromJsonAsync<JsonElement>("/api/clients");
        Assert.Contains(listed.EnumerateArray(), item => item.GetProperty("id").GetGuid() == client.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("Classic")]
    [InlineData("Essential")]
    [InlineData("Bioconfort")]
    [InlineData("Signature")]
    public async Task Projects_CreateGetList_UseStringEnumsAndNullSg(string productLine)
    {
        using var clientResponse = await fixture.Http.PostAsJsonAsync("/api/clients", new { name = "Cliente Demo" });
        var client = await clientResponse.Content.ReadFromJsonAsync<JsonElement>();
        var request = new { clientId = client.GetProperty("id").GetGuid(), name = " Casa ", city = " Ciudad ", productLine };
        using var created = await fixture.Http.PostAsJsonAsync("/api/projects", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, project.EnumerateObject().Count());
        Assert.True(project.GetProperty("isCreatedByMe").GetBoolean());
        Assert.Equal(request.clientId, project.GetProperty("clientId").GetGuid());
        Assert.Equal("Casa", project.GetProperty("name").GetString());
        Assert.Equal("Ciudad", project.GetProperty("city").GetString());
        Assert.Equal(productLine, project.GetProperty("productLine").GetString());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("sgCode").ValueKind);
        using var fetched = await fixture.Http.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(project.GetRawText(), (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var listed = await fixture.Http.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Contains(listed.EnumerateArray(), item => item.GetProperty("id").GetGuid() == project.GetProperty("id").GetGuid());
        using var duplicate = await fixture.Http.PostAsJsonAsync("/api/projects", request);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/projects")]
    public async Task Get_UnknownId_Returns404(string route)
    {
        using var response = await fixture.Http.GetAsync(route + "/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Project_WithMissingClient_Returns404WithoutWriting()
    {
        var before = fixture.Projects.Items.Count;
        using var response = await fixture.Http.PostAsJsonAsync("/api/projects",
            new { clientId = Guid.NewGuid(), name = "Casa", city = "Ciudad", productLine = "Classic" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, fixture.Projects.Items.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\"   \"}")]
    [InlineData("{\"name\":\"Cliente\",\"id\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"name\":\"Cliente\",\"createdAt\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"name\":\"Cliente\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{")]
    public async Task Clients_InvalidRequest_Returns400WithoutWriting(string json)
    {
        var before = fixture.Clients.Items.Count;
        using var response = await fixture.Http.PostAsync("/api/clients", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, fixture.Clients.Items.Count);
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("\"Classic, Essential\"")]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public async Task Projects_InvalidEnum_Returns400(string enumJson)
    {
        var json = "{\"clientId\":\"" + Guid.NewGuid() + "\",\"name\":\"Casa\",\"city\":\"Ciudad\",\"productLine\":" + enumJson + "}";
        using var response = await fixture.Http.PostAsync("/api/projects", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000000\",\"name\":\"Casa\",\"city\":\"Ciudad\",\"productLine\":\"Classic\"}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\" \",\"city\":\"Ciudad\",\"productLine\":\"Classic\"}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Casa\",\"city\":\" \",\"productLine\":\"Classic\"}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Casa\",\"city\":\"Ciudad\"}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Casa\",\"city\":\"Ciudad\",\"productLine\":\"Classic\",\"sgCode\":\"SG1043\"}")]
    public async Task Projects_InvalidInput_Returns400WithoutWriting(string json)
    {
        var before = fixture.Projects.Items.Count;
        using var response = await fixture.Http.PostAsync("/api/projects", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, fixture.Projects.Items.Count);
    }

    [Theory]
    [InlineData("/api/clients/not-a-guid")]
    [InlineData("/api/projects/not-a-guid")]
    public async Task InvalidGuid_Returns400(string route)
    {
        using var response = await fixture.Http.GetAsync(route);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsGeneric500WithoutInternalDetails()
    {
        fixture.Clients.FailReads = true;
        try
        {
            using var response = await fixture.Http.GetAsync("/api/clients/" + Guid.NewGuid());
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SQL", body);
            Assert.DoesNotContain("InvalidOperationException", body);
            Assert.DoesNotContain("StackTrace", body);
        }
        finally
        {
            fixture.Clients.FailReads = false;
        }
    }
}
