using System.Text.Json;
using SnG.Control.Application.Features.Management;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Application;

public class ManagementUseCaseTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
    private readonly ProjectStoreDouble projects = new();
    private readonly ClientStoreDouble clients = new();
    private readonly ProjectTaskStoreDouble tasks = new();
    private readonly TaskTiming timing = new(new ControlledTimeProvider(Now), new());

    private Project Project(string name = "Casa")
    {
        var client = new Client("Cliente " + name); clients.Items.Add(client);
        var project = new Project(client.Id, name, "City", ProductLine.Classic, "synthetic-creator");
        project.SetSgCode("SG005"); projects.Items.Add(project); return project;
    }
    private ProjectTask Task(Project project, LeadTimeStatus status, OperationalArea area = OperationalArea.Budgets)
    {
        var task = new ProjectTask(project.Id, area, area == OperationalArea.Budgets ? ProjectTaskType.PrepareQuotation : ProjectTaskType.ReviewQuotation,
            "synthetic-assignee", Now.AddDays(-15));
        if (status != LeadTimeStatus.NotStarted)
        {
            var deadline = status switch
            {
                LeadTimeStatus.Overdue => Now.AddDays(-1),
                LeadTimeStatus.CompletedLate or LeadTimeStatus.CompletedOnTime => Now.AddDays(-2),
                LeadTimeStatus.DueToday => Now.Date.AddHours(22),
                LeadTimeStatus.DueSoon => Now.Date.AddDays(2).AddHours(22),
                _ => Now.AddDays(10)
            };
            task.Start(Now.AddDays(-10), deadline);
            if (status == LeadTimeStatus.UntrackedLegacy)
                typeof(ProjectTask).GetProperty(nameof(ProjectTask.DeadlineAt))!.SetValue(task, null);
            if (status is LeadTimeStatus.CompletedLate or LeadTimeStatus.CompletedOnTime)
                task.Complete(status == LeadTimeStatus.CompletedLate ? deadline.AddTicks(1) : deadline);
        }
        tasks.Items.Add(task); return task;
    }
    [Fact]
    public async Task Overview_CountsEveryOfficialStatusAndOnlyRepresentedAreas_WithoutMutations()
    {
        var budgets = Project("A"); var commercial = Project("B"); Project("No tasks");
        foreach (var status in Enum.GetValues<LeadTimeStatus>())
            Task(status is LeadTimeStatus.CompletedLate or LeadTimeStatus.CompletedOnTime ? commercial : budgets,
                status, status is LeadTimeStatus.CompletedLate or LeadTimeStatus.CompletedOnTime ? OperationalArea.Commercial : OperationalArea.Budgets);
        var before = JsonSerializer.Serialize(new { projects.Items, Tasks = tasks.Items, tasks.Notifications });
        var store = new ManagementReadStoreDouble(projects, clients, tasks);
        using var cancellation = new CancellationTokenSource();
        var result = await new GetManagementOverviewUseCase(store, new(store, timing)).ExecuteAsync(cancellation.Token);
        Assert.Equal(3, result.TotalProjects);
        Assert.Equal(new ManagementTaskCounts(1, 5, 2), result.Tasks);
        Assert.Equal(new ManagementLeadTimeCounts(1, 1, 1, 1, 1, 1, 1, 1), result.LeadTime);
        Assert.Equal(new[] { new ManagementAreaSummary(OperationalArea.Commercial, 0, 0, 2, 0, 0, 0),
            new ManagementAreaSummary(OperationalArea.Budgets, 1, 5, 0, 1, 1, 1) }, result.Areas);
        Assert.Equal(3, result.Attention.Total);
        Assert.Equal(new[] { LeadTimeStatus.Overdue, LeadTimeStatus.DueToday, LeadTimeStatus.DueSoon }, result.Attention.Items.Select(t => t.LeadTimeStatus));
        Assert.All(result.Attention.Items, item =>
        {
            var task = tasks.Items.Single(t => t.Id == item.TaskId);
            Assert.Equal(task.ProjectId, item.ProjectId); Assert.Equal(budgets.Name, item.ProjectName);
            Assert.Equal("Cliente A", item.ClientName); Assert.Equal("SG005", item.SgCode);
            Assert.Equal(task.Type, item.TaskType); Assert.Equal(task.Status, item.TaskStatus);
            Assert.Equal(task.DeadlineAt, item.DeadlineAt); Assert.Equal(task.StartedAt, item.StartedAt);
        });
        Assert.Equal(before, JsonSerializer.Serialize(new { projects.Items, Tasks = tasks.Items, tasks.Notifications }));
        Assert.Equal(1, store.CountCalls); Assert.Equal(1, store.ListCalls);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
    }
    [Fact]
    public async Task Attention_TotalIsUncapped_ItemsAreTen_InPriorityDeadlineOrdinalNameAndIdOrder()
    {
        var z = Project("Z"); var a = Project("a");
        var soon = Task(a, LeadTimeStatus.DueSoon);
        var today = Task(z, LeadTimeStatus.DueToday);
        var early = Task(a, LeadTimeStatus.Overdue);
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.DeadlineAt))!.SetValue(early, Now.AddDays(-2));
        var tieA = Task(z, LeadTimeStatus.Overdue); var tieB = Task(z, LeadTimeStatus.Overdue);
        var lower = Task(a, LeadTimeStatus.Overdue);
        for (var i = 0; i < 9; i++) Task(a, LeadTimeStatus.DueSoon);
        var store = new ManagementReadStoreDouble(projects, clients, tasks);
        var result = await new GetManagementOverviewUseCase(store, new(store, timing)).ExecuteAsync(default);
        Assert.Equal(15, result.Attention.Total); Assert.Equal(10, result.Attention.Items.Count);
        Assert.Equal(new[] { early.Id }.Concat(new[] { tieA.Id, tieB.Id }.Order()).Append(lower.Id).Append(today.Id),
            result.Attention.Items.Take(5).Select(t => t.TaskId));
        Assert.All(result.Attention.Items.Skip(5), t => Assert.Equal(LeadTimeStatus.DueSoon, t.LeadTimeStatus));
    }
    [Fact]
    public async Task List_PreservesContextAndOfficialTiming_OrdersCurrentThenCompleted_WithoutWrites()
    {
        var project = Project();
        foreach (var status in Enum.GetValues<LeadTimeStatus>().Reverse()) Task(project, status);
        var completedLegacy = Task(project, LeadTimeStatus.UntrackedLegacy); completedLegacy.Complete(Now);
        var newerLate = Task(project, LeadTimeStatus.CompletedLate);
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.CompletedAt))!.SetValue(newerLate, Now.AddDays(-1));
        var before = JsonSerializer.Serialize(tasks.Items);
        var store = new ManagementReadStoreDouble(projects, clients, tasks);
        var result = await new ListManagementTasksUseCase(store, timing).ExecuteAsync(default);
        Assert.Equal(new[] { LeadTimeStatus.Overdue, LeadTimeStatus.DueToday, LeadTimeStatus.DueSoon, LeadTimeStatus.OnTrack,
            LeadTimeStatus.NotStarted, LeadTimeStatus.UntrackedLegacy, LeadTimeStatus.CompletedLate, LeadTimeStatus.CompletedLate,
            LeadTimeStatus.CompletedOnTime, LeadTimeStatus.UntrackedLegacy }, result.Select(t => t.LeadTimeStatus));
        Assert.Equal(newerLate.Id, result[6].TaskId); Assert.Equal(completedLegacy.Id, result[^1].TaskId);
        Assert.All(result, row =>
        {
            var source = tasks.Items.Single(t => t.Id == row.TaskId);
            Assert.Equal(project.Id, row.ProjectId); Assert.Equal(project.Name, row.ProjectName);
            Assert.Equal(project.ClientId, row.ClientId); Assert.Equal("Cliente Casa", row.ClientName);
            Assert.Equal(project.SgCode, row.SgCode); Assert.Equal(project.City, row.City); Assert.Equal(project.ProductLine, row.ProductLine);
            Assert.Equal(source.Area, row.Area); Assert.Equal(source.Type, row.Type); Assert.Equal(source.Status, row.Status);
            Assert.Equal(source.AssignedAt, row.AssignedAt); Assert.Equal(source.StartedAt, row.StartedAt);
            Assert.Equal(source.CompletedAt, row.CompletedAt); Assert.Equal(source.DeadlineAt, row.DeadlineAt); Assert.Equal(source.WasOverdue, row.WasOverdue);
            Assert.Equal(timing.Status(source, Now), row.LeadTimeStatus);
        });
        Assert.DoesNotContain("subject", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(tasks.Items)); Assert.Empty(tasks.Notifications);
        Assert.Equal(0, store.CountCalls); Assert.Equal(1, store.ListCalls);
        tasks.Items.Reverse();
        Assert.Equal(result, await new ListManagementTasksUseCase(store, timing).ExecuteAsync(default));
    }
    [Fact]
    public async Task EmptyOverview_HasNoInventedAreasOrProjectStates()
    {
        var store = new ManagementReadStoreDouble(projects, clients, tasks);
        var result = await new GetManagementOverviewUseCase(store, new(store, timing)).ExecuteAsync(default);
        Assert.Equal(0, result.TotalProjects); Assert.Empty(result.Areas); Assert.Empty(result.Attention.Items);
        Assert.Equal(new ManagementTaskCounts(0, 0, 0), result.Tasks);
        Assert.Equal(new ManagementLeadTimeCounts(0, 0, 0, 0, 0, 0, 0, 0), result.LeadTime);
        Assert.Equal(0, result.Attention.Total);
    }
}
