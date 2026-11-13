using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Api;

public class AssignSgCodeHttpTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Assignment_Correction_AndRepeat_ReturnFullConsistentResponse()
    {
        var project = SentProject();
        using var assigned = await fixture.BudgetsHttp.PutAsJsonAsync(Path(project.Id), new { sgCode = " sg0012 " });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var first = await assigned.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, first.EnumerateObject().Count());
        Assert.Equal("SG0012", first.GetProperty("project").GetProperty("sgCode").GetString());
        var task = first.GetProperty("prepareQuotationTask");
        Assert.Equal("InProgress", task.GetProperty("status").GetString());
        Assert.EndsWith("Z", task.GetProperty("startedAt").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        using var corrected = await fixture.BudgetsHttp.PutAsJsonAsync(Path(project.Id), new { sgCode = "sg0042" });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var second = await corrected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SG0042", second.GetProperty("project").GetProperty("sgCode").GetString());
        Assert.Equal(task.GetRawText(), second.GetProperty("prepareQuotationTask").GetRawText());
        using var repeated = await fixture.BudgetsHttp.PutAsJsonAsync(Path(project.Id), new { sgCode = " SG0042 " });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(second.GetRawText(), (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        Assert.Single(fixture.Tasks.Items, item => item.ProjectId == project.Id);
    }

    [Fact]
    public async Task DuplicateSg_Returns409AndLeavesLosingProjectPending()
    {
        var first = SentProject();
        var second = SentProject();
        using var winner = await fixture.BudgetsHttp.PutAsJsonAsync(Path(first.Id), new { sgCode = "SG99001" });
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        using var loser = await fixture.BudgetsHttp.PutAsJsonAsync(Path(second.Id), new { sgCode = " sg99001 " });
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Null(fixture.Projects.Items.Single(project => project.Id == second.Id).SgCode);
        var task = fixture.Tasks.Items.Single(task => task.ProjectId == second.Id);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
        Assert.Null(task.StartedAt);
        Assert.DoesNotContain("Npgsql", await loser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingOrUnsentProject_Returns404Or409()
    {
        using var missing = await fixture.BudgetsHttp.PutAsJsonAsync(Path(Guid.NewGuid()), new { sgCode = "SG1" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var project = new Project(Guid.NewGuid(), "Unsent", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        using var unsent = await fixture.BudgetsHttp.PutAsJsonAsync(Path(project.Id), new { sgCode = "SG1" });
        Assert.Equal(HttpStatusCode.Conflict, unsent.StatusCode);
        Assert.DoesNotContain(fixture.Tasks.Items, task => task.ProjectId == project.Id);
    }

    [Fact]
    public async Task InconsistentWorkflow_Returns409()
    {
        var project = SentProject();
        fixture.Tasks.Items.Single(task => task.ProjectId == project.Id).Start();
        using var response = await fixture.BudgetsHttp.PutAsJsonAsync(Path(project.Id), new { sgCode = "SG1" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(project.SgCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sgCode\":null}")]
    [InlineData("{\"sgCode\":\"\"}")]
    [InlineData("{\"sgCode\":\"   \"}")]
    [InlineData("{\"sgCode\":\"SG-1043\"}")]
    [InlineData("{\"sgCode\":\"SG١٢\"}")]
    [InlineData("{\"sgCode\":42}")]
    [InlineData("{\"sgCode\":\"SG1\",\"startedAt\":\"2026-01-01T00:00:00Z\"}")]
    public async Task InvalidRequests_Return400WithoutChangingWorkflow(string json)
    {
        var project = SentProject();
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await fixture.BudgetsHttp.PutAsync(Path(project.Id), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(project.SgCode);
        Assert.Equal(ProjectTaskStatus.Pending, fixture.Tasks.Items.Single(task => task.ProjectId == project.Id).Status);
    }

    private Project SentProject()
    {
        var project = new Project(Guid.NewGuid(), "Project", "City", ProductLine.Classic, "google-test-subject");
        fixture.Projects.Items.Add(project);
        fixture.Tasks.Items.Add(new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"));
        return project;
    }

    private static string Path(Guid id) => $"/api/projects/{id}/sg-code";
}
