using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Application;

public class NotificationSweepTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task NormalSweeps_CreateEachMilestoneOnce_ResolveAndPreserveHistory()
    {
        var scenario = new Scenario(); await scenario.Sweep(); Assert.Single(scenario.History);
        scenario.Clock.Now = scenario.Timing.DueSoon(scenario.Task.DeadlineAt!.Value); await scenario.Sweep(); await scenario.Sweep();
        Assert.Equal(2, scenario.History.Count);
        scenario.Clock.Now = scenario.Timing.DueToday(scenario.Task.DeadlineAt.Value); await scenario.Sweep(); await scenario.Sweep();
        Assert.Equal(3, scenario.History.Count);
        scenario.Clock.Now = scenario.Task.DeadlineAt.Value; await scenario.Sweep(); Assert.False(scenario.Task.WasOverdue);
        scenario.Clock.Now = scenario.Clock.Now.AddTicks(10); await scenario.Sweep(); await scenario.Sweep();
        Assert.Equal(4, scenario.History.Count); Assert.True(scenario.Task.WasOverdue);
        var overdue = Assert.Single(scenario.History, n => n.Kind == NotificationKind.Overdue); var created = overdue.CreatedAt;
        scenario.Clock.Now = scenario.Clock.Now.AddHours(2); scenario.Task.Complete(scenario.Clock.Now); await scenario.Sweep();
        Assert.Equal(NotificationState.Resolved, overdue.State); Assert.Equal(scenario.Task.CompletedAt, overdue.ResolvedAt);
        Assert.Equal(created, overdue.CreatedAt); Assert.Equal(scenario.Task.DeadlineAt, overdue.ScheduledAt);
        scenario.Clock.Now = scenario.Clock.Now.AddYears(1); await scenario.Sweep(); Assert.Equal(4, scenario.History.Count);
    }
    [Theory]
    [InlineData(0, NotificationKind.DueInTwoBusinessDays)]
    [InlineData(1, NotificationKind.DueToday)]
    [InlineData(2, NotificationKind.Overdue)]
    public async Task Recovery_OnlyCreatesMostRelevantMissedTemporalEvent(int stage, NotificationKind kind)
    {
        var scenario = new Scenario(); var deadline = scenario.Task.DeadlineAt!.Value;
        scenario.Clock.Now = (stage == 0 ? scenario.Timing.DueSoon(deadline) : stage == 1 ? scenario.Timing.DueToday(deadline) : deadline).AddMinutes(80);
        await scenario.Sweep(); await scenario.Sweep();
        Assert.Equal(2, scenario.History.Count);
        var temporal = Assert.Single(scenario.History, n => n.Kind != NotificationKind.TaskAssigned);
        Assert.Equal(kind, temporal.Kind); Assert.Equal(scenario.Clock.Now, temporal.CreatedAt); Assert.True(temporal.CreatedAt > temporal.ScheduledAt);
        Assert.Equal(Start, Assert.Single(scenario.History, n => n.Kind == NotificationKind.TaskAssigned).ScheduledAt);
    }
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task DueDay_BeginsAtEightBogota(long ticks, bool expected)
    {
        var scenario = new Scenario(); scenario.Clock.Now = scenario.Timing.DueToday(scenario.Task.DeadlineAt!.Value).AddTicks(ticks);
        await scenario.Sweep(); Assert.Equal(expected, scenario.History.Any(n => n.Kind == NotificationKind.DueToday));
    }
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Sweep_UsesStrictClockBoundaryWithoutRoundingBeforeComparison(long ticks, bool expected)
    {
        var scenario = new Scenario(); scenario.Clock.Now = scenario.Task.DeadlineAt!.Value.AddTicks(ticks);
        await scenario.Sweep(); Assert.Equal(expected, scenario.Task.WasOverdue);
        Assert.Equal(expected, scenario.History.Any(n => n.Kind == NotificationKind.Overdue));
    }
    [Theory]
    [InlineData(-10, false)]
    [InlineData(0, false)]
    [InlineData(10, true)]
    public async Task CompletedBeforeSweep_CreatesOnlyLateEvidenceWhenNeeded(long ticks, bool late)
    {
        var scenario = new Scenario(); scenario.Task.Complete(scenario.Task.DeadlineAt!.Value.AddTicks(ticks));
        scenario.Clock.Now = scenario.Task.DeadlineAt.Value.AddDays(1); await scenario.Sweep();
        Assert.Equal(late ? 1 : 0, scenario.History.Count);
        if (late)
        {
            var alert = Assert.Single(scenario.History); Assert.Equal(NotificationKind.Overdue, alert.Kind);
            Assert.Equal(NotificationState.Resolved, alert.State); Assert.Equal(scenario.Task.CompletedAt, alert.ResolvedAt);
        }
        Assert.Equal(late, scenario.Task.WasOverdue);
    }
    [Fact]
    public async Task ClockCorrectionAfterOverdue_PreservesLateFlagAndActualResolutionTime()
    {
        var scenario = new Scenario(); var deadline = scenario.Task.DeadlineAt!.Value;
        scenario.Clock.Now = deadline.AddMinutes(1); await scenario.Sweep();
        scenario.Clock.Now = deadline.AddSeconds(-1); scenario.Task.Complete(scenario.Clock.Now); await scenario.Sweep();
        Assert.True(scenario.Task.WasOverdue);
        var alert = Assert.Single(scenario.History, n => n.Kind == NotificationKind.Overdue);
        Assert.Equal(NotificationState.Resolved, alert.State); Assert.Equal(scenario.Task.CompletedAt, alert.ResolvedAt); Assert.Equal(deadline, alert.ScheduledAt);
    }
    [Fact]
    public async Task ConcurrentSweeps_AreIdempotent_WithControlledClock()
    {
        var scenario = new Scenario(); scenario.Clock.Now = scenario.Task.DeadlineAt!.Value.AddHours(1);
        await Task.WhenAll(scenario.Sweep(), scenario.Sweep());
        Assert.Equal(2, scenario.History.Count); Assert.Equal(2, scenario.History.Select(n => n.EventKey).Distinct().Count());
    }
    [Fact]
    public async Task PendingAndLegacy_RecoverOnlyCurrentAssignments()
    {
        var scenario = new Scenario(false); await scenario.Sweep(); await scenario.Sweep();
        Assert.Single(scenario.History); Assert.Null(scenario.Task.DeadlineAt); Assert.Null(scenario.Task.WasOverdue);
        scenario.History.Clear();
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.Status))!.SetValue(scenario.Task, ProjectTaskStatus.Completed);
        await scenario.Sweep(); Assert.Empty(scenario.History);
    }
    [Theory]
    [InlineData(ProjectTaskType.PrepareQuotation)]
    [InlineData(ProjectTaskType.ReviewQuotation)]
    [InlineData(ProjectTaskType.PresentQuotationToClient)]
    public void AssignmentAndOperationalTexts_AreSpecificToEveryTask(ProjectTaskType type)
    {
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Commercial, type, "assigned-subject", Start);
        var notification = Assert.Single(new ProjectNotificationPlanner(TestTiming.Value).Reconcile(task, [], Start));
        Assert.Equal("assigned-subject", notification.RecipientGoogleSubject); Assert.Equal(NotificationState.Informational, notification.State);
        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            if (kind >= NotificationKind.ProjectDocumentsUpdated)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ProjectNotificationPlanner.Title(type, kind));
                Assert.NotEqual(kind, notification.Kind);
                continue;
            }
            var title = ProjectNotificationPlanner.Title(type, kind); Assert.DoesNotContain(type.ToString(), title); Assert.DoesNotContain("Leak", title);
        }
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
    }
    [Fact]
    public void Projection_IsReadOnly_AndDistinguishesAllStates()
    {
        var scenario = new Scenario(false); var task = scenario.Task; var timing = scenario.Timing;
        Assert.Equal(LeadTimeStatus.NotStarted, timing.Status(task, Start)); timing.Start(task, Start); var deadline = task.DeadlineAt!.Value;
        Assert.Equal(LeadTimeStatus.OnTrack, timing.Status(task, Start));
        Assert.Equal(LeadTimeStatus.DueSoon, timing.Status(task, timing.DueSoon(deadline)));
        Assert.Equal(LeadTimeStatus.DueToday, timing.Status(task, deadline.Date.AddHours(5)));
        Assert.Equal(LeadTimeStatus.DueToday, timing.Status(task, deadline));
        Assert.Equal(LeadTimeStatus.Overdue, timing.Status(task, deadline.AddTicks(1))); Assert.False(task.WasOverdue); Assert.Empty(scenario.History);
        task.Complete(deadline); Assert.Equal(LeadTimeStatus.CompletedOnTime, timing.Status(task, deadline.AddYears(1)));
        var late = new Scenario(); late.Task.Complete(late.Task.DeadlineAt!.Value.AddTicks(1));
        Assert.Equal(LeadTimeStatus.CompletedLate, timing.Status(late.Task, deadline.AddYears(1)));
    }
    private sealed class Scenario : IProjectNotificationSweepStore
    {
        private readonly SemaphoreSlim gate = new(1);
        public ControlledTimeProvider Clock { get; } = new(Start);
        public TaskTiming Timing { get; }
        public ProjectTask Task { get; } = new(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "assigned-subject", Start);
        public List<ProjectNotification> History { get; } = [];
        public Scenario(bool start = true) { Timing = new(Clock, new()); if (start) Timing.Start(Task, Start); }
        public System.Threading.Tasks.Task Sweep() => new ProjectNotificationSweepService(this, Timing).ExecuteAsync(default);
        public async System.Threading.Tasks.Task SweepAsync(DateTime now, CancellationToken token)
        {
            await gate.WaitAsync(token);
            try { History.AddRange(new ProjectNotificationPlanner(Timing).Reconcile(Task, History, now)); }
            finally { gate.Release(); }
        }
    }
}
