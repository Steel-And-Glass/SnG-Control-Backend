using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class AuthenticationHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/clients")]
    [InlineData("POST", "/api/clients")]
    [InlineData("GET", "/api/projects")]
    [InlineData("GET", "/api/project-tasks?area=Budgets&status=Pending")]
    [InlineData("POST", "/api/projects/00000000-0000-0000-0000-000000000001/send-to-commercial")]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001/budget-quotation/commercial-file")]
    public async Task MissingAuthentication_Is401BeforeBusinessOrBodyValidation(string method, string route)
    {
        using var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsProfileAndManagementFlag()
    {
        using var response = await fixture.Http.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var user = json.RootElement;
        Assert.False(user.GetProperty("isManagement").GetBoolean());
        Assert.Equal(new[] { "subject", "email", "name", "isManagement", "roles" }, user.EnumerateObject().Select(property => property.Name));
        Assert.Equal("google-test-subject", user.GetProperty("subject").GetString());
        Assert.Equal("test@example.com", user.GetProperty("email").GetString());
        Assert.Equal("Test User", user.GetProperty("name").GetString());
        Assert.NotEqual(user.GetProperty("email").GetString(), user.GetProperty("subject").GetString());
    }

    [Theory]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("invalid")]
    public async Task InvalidIdentity_Is401WithoutEmailFallback(string mode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, mode);
        using var response = await fixture.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("subject-only")]
    [InlineData("unverified")]
    public async Task OptionalProfileClaims_DoNotAddAuthorizationRestrictions(string mode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, mode);
        using var response = await fixture.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("google-test-subject", json.RootElement.GetProperty("subject").GetString());
        if (mode == "subject-only")
        {
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("email").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("name").ValueKind);
        }
    }

    [Fact]
    public async Task ExistingBusinessEndpoint_StillWorksWhenAuthenticated()
    {
        using var response = await fixture.Http.GetAsync("/api/clients");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
