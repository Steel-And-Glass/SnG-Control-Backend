using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SnG.Control.Tests.Api;

public class ProjectTaskHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Workflow_IsExplicit_CreatesPendingTask_ListsHistory_AndRejectsDuplicate()
    {
        using var clientResponse = await fixture.Http.PostAsJsonAsync("/api/clients", new { name = "Cliente workflow" });
        var client = await clientResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var projectResponse = await fixture.Http.PostAsJsonAsync("/api/projects",
            new { clientId = client.GetProperty("id").GetGuid(), name = "Proyecto workflow", city = "Ciudad", productLine = "Classic" });
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = project.GetProperty("id").GetGuid();

        using var empty = await fixture.Http.GetAsync($"/api/projects/{id}/tasks");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.DoesNotContain(fixture.Tasks.Items, task => task.ProjectId == id);

        fixture.Documents.AddDocument(id);
        using var sent = await fixture.Http.PostAsync($"/api/projects/{id}/send-to-budgets", null);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var task = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(13, task.EnumerateObject().Count());
        Assert.Equal("budgets-test-subject", task.GetProperty("assignedToGoogleSubject").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("deadlineAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("wasOverdue").ValueKind);
        Assert.Equal("NotStarted", task.GetProperty("leadTimeStatus").GetString());
        Assert.NotEqual(Guid.Empty, task.GetProperty("id").GetGuid());
        Assert.Equal(id, task.GetProperty("projectId").GetGuid());
        Assert.Equal("Budgets", task.GetProperty("area").GetString());
        Assert.Equal("PrepareQuotation", task.GetProperty("type").GetString());
        Assert.Equal("Pending", task.GetProperty("status").GetString());
        Assert.EndsWith("Z", task.GetProperty("assignedAt").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        using var listed = await fixture.BudgetsHttp.GetAsync("/api/project-tasks?area=Budgets&status=Pending");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var tasks = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(tasks.EnumerateArray(), item => item.GetProperty("id").GetGuid() == task.GetProperty("id").GetGuid());

        using var history = await fixture.Http.GetAsync($"/api/projects/{id}/tasks");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(task.GetRawText(), Assert.Single((await history.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()).GetRawText());

        using var repeated = await fixture.Http.PostAsync($"/api/projects/{id}/send-to-budgets", null);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Single(fixture.Tasks.Items, item => item.ProjectId == id);
    }

    [Fact]
    public async Task MissingProject_Returns404ForSendAndHistory()
    {
        var id = Guid.NewGuid();
        var attempts = fixture.Tasks.InsertAttempts;
        using var sent = await fixture.Http.PostAsync($"/api/projects/{id}/send-to-budgets", null);
        using var history = await fixture.Http.GetAsync($"/api/projects/{id}/tasks");
        Assert.Equal(HttpStatusCode.NotFound, sent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        Assert.Equal(attempts, fixture.Tasks.InsertAttempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?area=Budgets")]
    [InlineData("?status=Pending")]
    [InlineData("?area=&status=Pending")]
    [InlineData("?area=Budgets&status=")]
    [InlineData("?area=Unknown&status=Pending")]
    [InlineData("?area=Budgets&status=Unknown")]
    [InlineData("?area=2&status=Pending")]
    [InlineData("?area=Budgets&status=1")]
    [InlineData("?area=Commercial,Budgets&status=Pending")]
    [InlineData("?area=Budgets&status=Pending,InProgress")]
    public async Task AreaQuery_RequiresSingleKnownNames(string query)
    {
        using var response = await fixture.Http.GetAsync("/api/project-tasks" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KnownAreaAndStatusWithoutMatches_ReturnsEmptyCollection()
    {
        using var response = await fixture.Http.GetAsync("/api/project-tasks?area=Commercial&status=Completed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    public async Task Tasks_CannotBeCreatedThroughGenericPost()
    {
        using var response = await fixture.Http.PostAsJsonAsync("/api/project-tasks", new { projectId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task InvalidProjectId_Returns400ForSendAndHistory()
    {
        using var sent = await fixture.Http.PostAsync("/api/projects/not-a-guid/send-to-budgets", null);
        using var history = await fixture.Http.GetAsync("/api/projects/not-a-guid/tasks");
        Assert.Equal(HttpStatusCode.BadRequest, sent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, history.StatusCode);
    }
}
