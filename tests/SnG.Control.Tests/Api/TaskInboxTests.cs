using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Api;

public class TaskInboxTests(MultiRoleFixture fixture) : IClassFixture<MultiRoleFixture>
{
    private HttpClient Client(string role)
    {
        var http = new HttpClient { BaseAddress = fixture.Http.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.SchemeName, "valid");
        http.DefaultRequestHeaders.Add("X-SnG-Active-Role", role);
        return http;
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Pending)]
    [InlineData(ProjectTaskStatus.InProgress)]
    [InlineData(ProjectTaskStatus.Completed)]
    public async Task SameSubject_OnlyItsActiveRoleAssignmentAppears_WhileProjectHistoryRemainsComplete(ProjectTaskStatus status)
    {
        var project = new Project(Guid.NewGuid(), "Synthetic inbox", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        var start = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        ProjectTask Add(ProjectTaskType type, OperationalArea area, string? assignee, ProjectTaskStatus taskStatus)
        {
            var task = new ProjectTask(project.Id, area, type, assignee, start);
            if (taskStatus != ProjectTaskStatus.Pending) task.Start(start, start.AddDays(6));
            if (taskStatus == ProjectTaskStatus.Completed) task.Complete(start.AddDays(1));
            fixture.Tasks.Items.Add(task); return task;
        }
        var matrix = new[] {
            ("CommercialAdvisor", ProjectTaskType.PresentQuotationToClient, OperationalArea.Commercial),
            ("Budgets", ProjectTaskType.PrepareQuotation, OperationalArea.Budgets),
            ("CommercialHead", ProjectTaskType.ReviewQuotation, OperationalArea.Commercial)
        };
        var expected = new Dictionary<string, Guid>();
        foreach (var (role, type, area) in matrix)
        {
            expected[role] = Add(type, area, "google-test-subject", status).Id;
            Add(type, area, "other-subject", status);
            Add(type, area, null, status);
            Add(type, area == OperationalArea.Commercial ? OperationalArea.Budgets : OperationalArea.Commercial, "google-test-subject", status);
            Add(type, area, "google-test-subject", status == ProjectTaskStatus.Pending ? ProjectTaskStatus.Completed : ProjectTaskStatus.Pending);
        }
        var historyIds = fixture.Tasks.Items.Where(t => t.ProjectId == project.Id).Select(t => t.Id).Order().ToArray();
        var before = JsonSerializer.Serialize(new { project, fixture.Tasks.Items });
        foreach (var (role, _, area) in matrix)
        {
            using var http = Client(role);
            var rows = await http.GetFromJsonAsync<JsonElement[]>($"/api/project-tasks?area={area}&status={status}");
            var task = Assert.Single(rows!, t => t.GetProperty("projectId").GetGuid() == project.Id);
            Assert.Equal(expected[role], task.GetProperty("id").GetGuid());
            var history = await http.GetFromJsonAsync<JsonElement[]>($"/api/projects/{project.Id}/tasks");
            var expectedHistory = role == "Budgets" ? fixture.Tasks.Items.Where(t => t.ProjectId == project.Id && t.Area == OperationalArea.Budgets).Select(t => t.Id).Order().ToArray() : historyIds;
            Assert.Equal(expectedHistory, history!.Select(t => t.GetProperty("id").GetGuid()).Order());
        }
        Assert.Equal(before, JsonSerializer.Serialize(new { project, fixture.Tasks.Items }));
    }

    [Theory]
    [InlineData("Budgets", "Commercial")]
    [InlineData("CommercialHead", "Budgets")]
    [InlineData("CommercialAdvisor", "Budgets")]
    [InlineData("Management", "Commercial")]
    [InlineData("Management", "Budgets")]
    public async Task IncompatibleAreaOrManagement_Returns403BeforeStoreRead(string role, string area)
    {
        using var http = Client(role);
        var reads = fixture.Tasks.ReceivedTokens.Count;
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/project-tasks?area={area}&status=InProgress")).StatusCode);
        Assert.Equal(reads, fixture.Tasks.ReceivedTokens.Count);
        if (role == "Management") Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/management/tasks")).StatusCode);
    }
}
