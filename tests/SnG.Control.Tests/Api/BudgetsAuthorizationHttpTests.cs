using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class BudgetsAuthorizationHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    public static TheoryData<string, string, string?, HttpStatusCode> AccessCases
    {
        get
        {
            var cases = new TheoryData<string, string, string?, HttpStatusCode>();
            foreach (var (method, route, allowedStatus) in new[]
            {
                ("PUT", "sg-code", HttpStatusCode.NotFound),
                ("POST", "budget-quotation/preview", HttpStatusCode.BadRequest),
                ("POST", "budget-quotation/confirm", HttpStatusCode.BadRequest),
                ("POST", "send-to-commercial", HttpStatusCode.NotFound)
            })
            {
                cases.Add(method, route, null, HttpStatusCode.Unauthorized);
                cases.Add(method, route, "other", HttpStatusCode.Forbidden);
                cases.Add(method, route, "valid", HttpStatusCode.Forbidden); // Commercial Head, same email as Budgets.
                cases.Add(method, route, "budgets", allowedStatus);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(AccessCases))]
    public async Task Operations_RequireBudgetsBeforeBusinessOrUploadValidation(
        string method, string route, string? mode, HttpStatusCode expected)
    {
        using var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/projects/{Guid.NewGuid()}/{route}");
        request.Headers.Add("X-SnG-Active-Role", "Budgets");
        if (mode is not null) request.Headers.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        if (method == "PUT") request.Content = JsonContent.Create(new { sgCode = "SG123" });
        else if (route.Contains("budget-quotation", StringComparison.Ordinal))
            request.Content = new StringContent("malformed upload", Encoding.UTF8, "multipart/form-data");
        var loads = fixture.BudgetsWorkflow.ReceivedTokens.Count;
        var saves = fixture.BudgetsWorkflow.SaveCalls;
        var confirmations = fixture.Confirmations.Calls;
        var sends = fixture.Handoff.SendCalls;
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Equal(loads, fixture.BudgetsWorkflow.ReceivedTokens.Count);
            Assert.Equal(saves, fixture.BudgetsWorkflow.SaveCalls);
            Assert.Equal(confirmations, fixture.Confirmations.Calls);
            Assert.Equal(sends, fixture.Handoff.SendCalls);
        }
    }

    [Theory]
    [InlineData("other", false)]
    [InlineData("valid", false)]
    [InlineData("other", true)]
    [InlineData("valid", true)]
    public async Task ForbiddenSg_LeavesProjectTaskAndNotificationsUntouched(string mode, bool correction)
    {
        var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        fixture.Documents.AddDocument(project.Id);
        using var sent = await fixture.Http.PostAsync($"/api/projects/{project.Id}/send-to-budgets", null);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        if (correction)
        {
            using var assigned = await fixture.BudgetsHttp.PutAsJsonAsync($"/api/projects/{project.Id}/sg-code", new { sgCode = "SG" + fixture.Projects.Items.Count });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        }
        string Snapshot() => JsonSerializer.Serialize(new
        {
            Project = fixture.Projects.Items.Single(p => p.Id == project.Id),
            Task = fixture.Tasks.Items.Single(t => t.ProjectId == project.Id),
            fixture.Tasks.Notifications
        });
        var before = Snapshot();
        var calls = fixture.BudgetsWorkflow.ReceivedTokens.Count;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{project.Id}/sg-code")
        {
            Content = JsonContent.Create(new { sgCode = "SG456" })
        };
        request.Headers.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        using var response = await fixture.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fixture.BudgetsWorkflow.ReceivedTokens.Count);
        Assert.Equal(before, Snapshot());
        if (!correction)
        {
            Assert.Null(fixture.Projects.Items.Single(p => p.Id == project.Id).SgCode);
            var task = fixture.Tasks.Items.Single(t => t.ProjectId == project.Id);
            Assert.Equal(ProjectTaskStatus.Pending, task.Status);
            Assert.Null(task.StartedAt);
            Assert.Null(task.DeadlineAt);
            Assert.Null(task.WasOverdue);
        }
    }

    [Theory]
    [InlineData("other")]
    [InlineData("valid")]
    public async Task QueriesRespectOwnership_AndSendRequiresAdvisorOwner(string mode)
    {
        using var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialAdvisor");
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        fixture.Documents.AddDocument(project.Id);
        using var sent = await client.PostAsync($"/api/projects/{project.Id}/send-to-budgets", null);
        Assert.Equal(mode == "valid" ? HttpStatusCode.Created : HttpStatusCode.Forbidden, sent.StatusCode);
        foreach (var route in new[] { "/api/clients", "/api/projects", $"/api/projects/{project.Id}",
            "/api/project-tasks?area=Budgets&status=Pending", $"/api/projects/{project.Id}/tasks",
            "/api/notifications", $"/api/projects/{project.Id}/notifications" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(route.StartsWith("/api/project-tasks") ? HttpStatusCode.Forbidden : mode == "other" && route.Contains(project.Id.ToString()) ? HttpStatusCode.NotFound : HttpStatusCode.OK, response.StatusCode);
        }
    }
}
