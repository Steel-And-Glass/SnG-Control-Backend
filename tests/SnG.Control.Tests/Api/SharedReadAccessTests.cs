using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class SharedReadAccessTests(MultiRoleFixture fixture) : IClassFixture<MultiRoleFixture>
{
    private HttpClient Client(string role)
    {
        var client = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "valid");
        client.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
        return client;
    }

    [Theory]
    [InlineData("CommercialAdvisor")]
    [InlineData("Budgets")]
    [InlineData("CommercialHead")]
    [InlineData("Management")]
    public async Task SameSubject_ListsAndDirectReadsShareScopeIncludingCompletedAssignments(string role)
    {
        var cases = new[] { "owner", "budget", "review", "foreign", "legacy" };
        var projects = cases.Select(kind => new Project(Guid.NewGuid(), kind, "Synthetic", ProductLine.Classic,
            kind == "owner" ? "google-test-subject" : "other-subject")).ToArray();
        foreach (var project in projects)
        {
            if (project.Name == "legacy") typeof(Project).GetProperty(nameof(Project.CreatedByGoogleSubject))!.SetValue(project, null);
            var client = new Client("Synthetic " + project.Name);
            typeof(Client).GetProperty(nameof(client.Id))!.SetValue(client, project.ClientId);
            fixture.Clients.Items.Add(client); fixture.Projects.Items.Add(project); fixture.Documents.AddDocument(project.Id);
            if (project.Name is "budget" or "review" or "foreign")
            {
                var type = project.Name == "review" ? ProjectTaskType.ReviewQuotation : ProjectTaskType.PrepareQuotation;
                var task = new ProjectTask(project.Id, type == ProjectTaskType.ReviewQuotation ? OperationalArea.Commercial : OperationalArea.Budgets,
                    type, project.Name == "foreign" ? "other-subject" : "google-test-subject");
                task.Start(); task.Complete(); fixture.Tasks.Items.Add(task);
            }
        }
        var before = JsonSerializer.Serialize(new { projects, tasks = fixture.Tasks.Items });
        using var http = Client(role);
        var listed = await http.GetFromJsonAsync<JsonElement[]>("/api/projects");
        var clients = await http.GetFromJsonAsync<JsonElement[]>("/api/clients");
        using var inbox = await http.GetAsync("/api/project-tasks?area=Budgets&status=Completed");
        Assert.Equal(role == "Budgets" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, inbox.StatusCode);
        var areaTasks = role == "Budgets" ? await inbox.Content.ReadFromJsonAsync<JsonElement[]>() : [];
        foreach (var project in projects)
        {
            var visible = role switch { "Management" or "CommercialHead" => true, "CommercialAdvisor" => project.Name == "owner", "Budgets" => project.Name is "budget" or "foreign", _ => false };
            var resourceAllowed = role == "Management" || (role, project.Name) is ("CommercialAdvisor", "owner") or ("Budgets", "budget") or ("CommercialHead", "review");
            Assert.Equal(visible, listed!.Any(p => p.GetProperty("id").GetGuid() == project.Id));
            var clientAllowed = visible || role == "CommercialAdvisor";
            Assert.Equal(clientAllowed, clients!.Any(c => c.GetProperty("id").GetGuid() == project.ClientId));
            Assert.Equal(clientAllowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await http.GetAsync($"/api/clients/{project.ClientId}")).StatusCode);
            foreach (var suffix in new[] { "", "/tasks", "/notifications" })
                Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await http.GetAsync($"/api/projects/{project.Id}{suffix}")).StatusCode);
            Assert.Equal(resourceAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await http.GetAsync($"/api/projects/{project.Id}/documents")).StatusCode);
            Assert.Equal(role == "Budgets" && project.Name == "budget", areaTasks!.Any(t => t.GetProperty("projectId").GetGuid() == project.Id));
        }
        Assert.Equal(before, JsonSerializer.Serialize(new { projects, tasks = fixture.Tasks.Items }));
    }

    [Fact]
    public async Task StatusAndOperationalDownloadRejectDifferentAssignedSubject()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync();
        var state = fixture.Handoff.State!;
        using var budget = Client("Budgets"); // Same account owns Budgets, but the task belongs to another Subject.
        Assert.Equal(HttpStatusCode.NotFound, (await budget.GetAsync($"/api/projects/{state.Project.Id}/budget-quotation/status")).StatusCode);
        fixture.Handoff.State = state with { Tasks = [new ProjectTask(state.Project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "other-subject")] };
        using var head = Client("CommercialHead");
        Assert.Equal(HttpStatusCode.NotFound, (await head.GetAsync($"/api/projects/{state.Project.Id}/budget-quotation/commercial-file")).StatusCode);
        Assert.Empty(fixture.Handoff.SavedKeys);
    }
}
