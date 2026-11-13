using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Api;

public class NotificationHttpTests
{
    [Theory]
    [InlineData("/api/notifications")]
    [InlineData("/api/projects/00000000-0000-0000-0000-000000000001/notifications")]
    public async Task Notifications_RequireAuthentication(string url)
    {
        var fixture = new ApiFixture(); await fixture.InitializeAsync();
        try { fixture.Http.DefaultRequestHeaders.Authorization = null; Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Http.GetAsync(url)).StatusCode); }
        finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public async Task PersonalAndProjectHistory_HaveCorrectScopeOrderAndSafeContract()
    {
        var fixture = new ApiFixture(); await fixture.InitializeAsync();
        try
        {
            var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject"); fixture.Projects.Items.Add(project);
            var now = new DateTime(2026, 10, 20, 23, 0, 0, DateTimeKind.Utc); var taskId = Guid.NewGuid();
            ProjectNotificationResponse Notice(string recipient, NotificationState state, int hour) => new(Guid.NewGuid(), project.Id, taskId, recipient,
                ProjectTaskType.ReviewQuotation, state == NotificationState.Informational ? NotificationKind.TaskAssigned : NotificationKind.Overdue,
                state, "Aviso operativo", "Detalle del proyecto", now.AddHours(hour - 1), now.AddHours(hour), state == NotificationState.Resolved ? now.AddDays(1) : null, now);
            fixture.Notifications.Extra.AddRange([Notice("google-test-subject", NotificationState.Active, 1), Notice("other-subject", NotificationState.Informational, 2),
                Notice("google-test-subject", NotificationState.Informational, 0), Notice("google-test-subject", NotificationState.Resolved, 3)]);
            var mine = (await fixture.Http.GetFromJsonAsync<ProjectNotificationResponse[]>("/api/notifications", JsonOptions()))!;
            Assert.Equal(3, mine.Length); Assert.All(mine, n => Assert.Equal("google-test-subject", n.RecipientGoogleSubject));
            Assert.Equal(mine.OrderByDescending(n => n.CreatedAt), mine); Assert.Equal(3, mine.Select(n => n.State).Distinct().Count());
            var history = (await fixture.Http.GetFromJsonAsync<ProjectNotificationResponse[]>($"/api/projects/{project.Id}/notifications", JsonOptions()))!;
            Assert.Equal(4, history.Length); Assert.Equal(history.OrderBy(n => n.CreatedAt), history); Assert.Contains(history, n => n.ResolvedAt != null);
            var json = await fixture.Http.GetStringAsync("/api/notifications");
            foreach (var forbidden in new[] { "eventKey", "token", "clientId", "Authorization" }) Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
            foreach (var query in new[] { "subject=other-subject", "userId=other", "email=test@example.com" })
                Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Http.GetAsync("/api/notifications?" + query)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.GetAsync($"/api/projects/{Guid.NewGuid()}/notifications")).StatusCode);
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public async Task AssignmentStartsPending_FirstSgFreezesDeadline_AndGetNeverPersistsOverdue()
    {
        var clock = new ControlledTimeProvider(new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc));
        var fixture = new ApiFixture { Clock = clock }; await fixture.InitializeAsync();
        try
        {
            var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject"); fixture.Projects.Items.Add(project);
            fixture.Documents.AddDocument(project.Id);
            var send = await fixture.Http.PostAsync($"/api/projects/{project.Id}/send-to-budgets", null); Assert.Equal(HttpStatusCode.Created, send.StatusCode);
            var pending = Assert.Single(fixture.Tasks.Items); Assert.Equal(clock.Now, pending.AssignedAt); Assert.Null(pending.DeadlineAt); Assert.Null(pending.WasOverdue);
            var assigned = Assert.Single(fixture.Tasks.Notifications); Assert.Equal("budgets-test-subject", assigned.RecipientGoogleSubject);
            Assert.Equal(HttpStatusCode.OK, (await fixture.BudgetsHttp.PutAsJsonAsync($"/api/projects/{project.Id}/sg-code", new { sgCode = "SG90001" })).StatusCode);
            var task = Assert.Single(fixture.Tasks.Items); var deadline = task.DeadlineAt; var start = task.StartedAt;
            Assert.Equal(new DateTime(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc), deadline); Assert.False(task.WasOverdue);
            clock.Now = deadline!.Value.AddHours(1);
            var correction = await fixture.BudgetsHttp.PutAsJsonAsync($"/api/projects/{project.Id}/sg-code", new { sgCode = "SG90002" }); Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
            task = Assert.Single(fixture.Tasks.Items); Assert.Equal(deadline, task.DeadlineAt); Assert.Equal(start, task.StartedAt); Assert.False(task.WasOverdue);
            var response = (await fixture.Http.GetFromJsonAsync<JsonElement>($"/api/projects/{project.Id}/tasks"))[0];
            Assert.Equal("Overdue", response.GetProperty("leadTimeStatus").GetString()); Assert.False(task.WasOverdue); Assert.Single(fixture.Tasks.Notifications);
        }
        finally { await fixture.DisposeAsync(); }
    }
    private static JsonSerializerOptions JsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); return options; }
}
