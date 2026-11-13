using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Api;

public class CommercialIdentityHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ConcurrentRequestsAndRetry_KeepOneAssignedReview()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var id = fixture.Handoff.State!.Project.Id;
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "Budgets");
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "budgets");
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => fixture.Http.PostAsync($"/api/projects/{id}/send-to-commercial", null)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var review = Assert.Single(fixture.Handoff.State!.Tasks, task => task.Type == ProjectTaskType.ReviewQuotation);
            Assert.Equal("google-test-subject", review.AssignedToGoogleSubject);
            Assert.Equal(ProjectTaskStatus.InProgress, review.Status); Assert.NotNull(review.StartedAt);
            var snapshot = JsonSerializer.Serialize(fixture.Handoff.State);
            using var retry = await fixture.Http.PostAsync($"/api/projects/{id}/send-to-commercial", null);
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            Assert.Equal(snapshot, JsonSerializer.Serialize(fixture.Handoff.State)); Assert.Single(fixture.Handoff.SavedKeys);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, 0)]
    [InlineData("missing-sub", HttpStatusCode.Unauthorized, 0)]
    [InlineData("empty-sub", HttpStatusCode.Unauthorized, 0)]
    [InlineData("other", HttpStatusCode.Forbidden, 0)]
    [InlineData("valid", HttpStatusCode.NotFound, 1)]
    [InlineData("head-other-email", HttpStatusCode.NotFound, 1)]
    public async Task Download_AuthorizesBeforeReadingState(string? mode, HttpStatusCode expected, int calls)
    {
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialHead");
        fixture.Handoff = new();
        fixture.Http.DefaultRequestHeaders.Authorization = mode is null ? null : new(TestAuthenticationHandler.SchemeName, mode);
        using var response = await fixture.Http.GetAsync($"/api/projects/{Guid.NewGuid()}/budget-quotation/commercial-file");
        Assert.Equal(expected, response.StatusCode); Assert.Equal(calls, fixture.Handoff.GetFileCalls);
        Assert.Empty(fixture.Handoff.SavedKeys);
    }

    [Fact]
    public async Task Budgets_CanSendAndHeadCanDownloadWithChangedEmail()
    {
        fixture.Handoff = await CommercialHandoffScenario.CreateAsync(); var id = fixture.Handoff.State!.Project.Id;
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "Budgets");
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "budgets");
        using var sent = await fixture.Http.PostAsync($"/api/projects/{id}/send-to-commercial", null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var body = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("google-test-subject", body.GetProperty("commercialTask").GetProperty("assignedToGoogleSubject").GetString());
        Assert.Equal("budgets-test-subject", body.GetProperty("budgetTask").GetProperty("assignedToGoogleSubject").GetString());
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialHead");
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "head-other-email");
        using var download = await fixture.Http.GetAsync($"/api/projects/{id}/budget-quotation/commercial-file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Theory]
    [InlineData("createdBy")]
    [InlineData("createdByGoogleSubject")]
    [InlineData("createdByEmail")]
    [InlineData("createdByName")]
    [InlineData("subject")]
    [InlineData("responsibleCommercialSubject")]
    public async Task Body_CannotSupplyIdentity(string field)
    {
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "valid");
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialAdvisor");
        var client = new Client("Synthetic"); fixture.Clients.Items.Add(client);
        var before = fixture.Projects.Items.Count;
        var body = new Dictionary<string, object> { ["clientId"] = client.Id, ["name"] = "Project", ["city"] = "City", ["productLine"] = "Classic", [field] = "other-google-subject" };
        using var response = await fixture.Http.PostAsJsonAsync("/api/projects", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, fixture.Projects.Items.Count);
    }

    [Theory]
    [InlineData("other", "other-google-subject", "test@example.com", "Test User")]
    [InlineData("subject-only", "google-test-subject", null, null)]
    public async Task CreateGetAndList_ExposeAuthenticatedCreator(string mode, string subject, string? email, string? name)
    {
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, mode);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialAdvisor");
        var client = new Client("Synthetic"); fixture.Clients.Items.Add(client);
        using var created = await fixture.Http.PostAsJsonAsync("/api/projects", new { clientId = client.Id, name = "Project", city = "City", productLine = "Classic" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = result.GetProperty("id").GetGuid();
        var creatorName = result.GetProperty("createdByName"); Assert.True(result.GetProperty("isCreatedByMe").GetBoolean());

        var stored = Assert.Single(fixture.Projects.Items, project => project.Id == id);
        Assert.Equal(subject, stored.CreatedByGoogleSubject); Assert.Equal(email, stored.CreatedByEmail); Assert.Equal(name, stored.CreatedByName);
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "CommercialHead");
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "head-other-email");
        fixture.Tasks.Items.Add(new ProjectTask(id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "google-test-subject"));
        var fetched = await fixture.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}");
        var listed = await fixture.Http.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(creatorName.GetRawText(), fetched.GetProperty("createdByName").GetRawText());
        Assert.Equal(creatorName.GetRawText(), Assert.Single(listed.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id).GetProperty("createdByName").GetRawText());
    }

    [Fact]
    public async Task LegacyCreator_AndTaskAssignments_AreSerializedInQueries()
    {
        fixture.Http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "management");
        fixture.Http.DefaultRequestHeaders.Remove("X-SnG-Active-Role");
        fixture.Http.DefaultRequestHeaders.Add("X-SnG-Active-Role", "Management");
        using var db = new SnG.Control.Infrastructure.Persistence.SnGControlDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SnG.Control.Infrastructure.Persistence.SnGControlDbContext>().UseNpgsql().Options);
        var project = LegacyProjectMaterializer.Create(db); fixture.Projects.Items.Add(project);
        var prepare = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        var legacyProject = LegacyProjectMaterializer.Create(db); fixture.Projects.Items.Add(legacyProject);
        var legacy = new ProjectTask(legacyProject.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation);
        var assigned = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "google-test-subject"); assigned.Start();
        fixture.Tasks.Items.AddRange([prepare, legacy, assigned]);
        var legacyTasks = await fixture.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{legacyProject.Id}/tasks");
        Assert.Equal(JsonValueKind.Null, Assert.Single(legacyTasks.EnumerateArray()).GetProperty("assignedToGoogleSubject").ValueKind);
        var fetched = await fixture.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{project.Id}");
        Assert.Equal(JsonValueKind.Null, fetched.GetProperty("createdByName").ValueKind);
        var projects = await fixture.Http.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(JsonValueKind.Null, Assert.Single(projects.EnumerateArray(), p => p.GetProperty("id").GetGuid() == project.Id).GetProperty("createdByName").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Http.GetAsync("/api/project-tasks?area=Commercial&status=InProgress")).StatusCode);
        foreach (var route in new[] { $"/api/projects/{project.Id}/tasks" })
        {
            var tasks = await fixture.Http.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal("google-test-subject", Assert.Single(tasks.EnumerateArray(), t => t.GetProperty("id").GetGuid() == assigned.Id).GetProperty("assignedToGoogleSubject").GetString());
            if (route.EndsWith("/tasks"))
                foreach (var task in tasks.EnumerateArray().Where(t => t.GetProperty("id").GetGuid() != assigned.Id))
                    Assert.Equal(JsonValueKind.Null, task.GetProperty("assignedToGoogleSubject").ValueKind);
        }
    }
}
