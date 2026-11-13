using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Application;

public class LeadTimeCalendarTests
{
    private readonly ColombiaBusinessCalendar calendar = new();
    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-01-12")]
    [InlineData("2026-03-23")]
    [InlineData("2026-04-02")]
    [InlineData("2026-04-03")]
    [InlineData("2026-05-01")]
    [InlineData("2026-05-18")]
    [InlineData("2026-06-08")]
    [InlineData("2026-06-15")]
    [InlineData("2026-06-29")]
    [InlineData("2026-07-13")]
    [InlineData("2026-07-20")]
    [InlineData("2026-08-07")]
    [InlineData("2026-08-17")]
    [InlineData("2026-10-12")]
    [InlineData("2026-11-02")]
    [InlineData("2026-11-16")]
    [InlineData("2026-12-08")]
    [InlineData("2026-12-25")]
    public void NationalHolidays2026_AreNotWorkingDays(string date) => Assert.False(calendar.IsBusinessDay(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-10-17", true)]
    [InlineData("2026-10-18", false)]
    [InlineData("2026-03-19", true)]
    [InlineData("2026-06-29", false)]
    [InlineData("2025-07-14", true)]
    [InlineData("2026-07-09", true)]
    public void BusinessDays_IncludeSaturdaysAndRespectObservedDates(string date, bool expected) => Assert.Equal(expected, calendar.IsBusinessDay(DateOnly.Parse(date)));

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2000, "2000-04-23")]
    [InlineData(2100, "2100-03-28")]
    public void GregorianEaster_KnownDates(int year, string date) => Assert.Equal(DateOnly.Parse(date), ColombiaBusinessCalendar.EasterSunday(year));

    [Theory]
    [InlineData("2026-10-14", "2026-10-20")]
    [InlineData("2026-10-09", "2026-10-16")]
    [InlineData("2026-03-31", "2026-04-08")]
    [InlineData("2026-12-29", "2027-01-05")]
    [InlineData("2026-07-08", "2026-07-15")]
    public void FiveDays_ExcludeStartAndCrossSundayHolidaysAndYear(string start, string expected)
    {
        var timing = new TaskTiming(TimeProvider.System, calendar);
        foreach (var type in new[] { ProjectTaskType.PrepareQuotation, ProjectTaskType.ReviewQuotation, ProjectTaskType.PresentQuotationToClient })
            Assert.Equal(calendar.AtLocalTime(DateOnly.Parse(expected), 17), timing.Deadline(type, calendar.AtLocalTime(DateOnly.Parse(start), 16)));
    }

    [Fact]
    public void ContractualOctober14_EndsOctober20At22Utc_WithSaturdayReminder()
    {
        var timing = new TaskTiming(TimeProvider.System, calendar);
        var deadline = timing.Deadline(ProjectTaskType.PrepareQuotation, new(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc), deadline);
        Assert.Equal(new DateTime(2026, 10, 17, 13, 0, 0, DateTimeKind.Utc), timing.DueSoon(deadline));
        Assert.Equal(new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc), timing.DueToday(deadline));
        Assert.Equal("America/Bogota", calendar.Zone.Id);
    }
    [Fact]
    public void Reminder_SkipsSundayAndHoliday_Backwards()
    {
        var timing = new TaskTiming(TimeProvider.System, calendar);
        Assert.Equal(calendar.AtLocalTime(new(2026, 10, 9), 8), timing.DueSoon(calendar.AtLocalTime(new(2026, 10, 13), 17)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskTiming.BusinessDays((ProjectTaskType)4));
        Assert.Throws<ArgumentException>(() => calendar.LocalDate(new(2026, 10, 14)));
    }
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Completion_UsesStrictDeadlineBoundary(long ticks, bool late)
    {
        var task = NewTask(); var start = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        var deadline = TestTiming.Value.Deadline(task.Type, start); task.Start(start, deadline);
        task.Complete(deadline.AddTicks(ticks));
        Assert.Equal(late, task.WasOverdue); Assert.Equal(deadline.AddTicks(ticks), task.CompletedAt);
    }
    [Fact]
    public void Overdue_IsMonotonic_AndLegacyNeverGetsInventedDeadline()
    {
        var task = NewTask(); var now = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        task.Start(now, now.AddDays(1)); task.DetectOverdue(now.AddDays(1)); Assert.False(task.WasOverdue);
        task.DetectOverdue(now.AddDays(1).AddTicks(1)); Assert.True(task.WasOverdue);
        task.Complete(now.AddDays(1)); Assert.True(task.WasOverdue);
        var legacy = NewTask();
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.Status))!.SetValue(legacy, ProjectTaskStatus.InProgress);
        typeof(ProjectTask).GetProperty(nameof(ProjectTask.StartedAt))!.SetValue(legacy, now);
        legacy.DetectOverdue(now.AddYears(1)); legacy.Complete(now.AddYears(1));
        Assert.Null(legacy.DeadlineAt); Assert.Null(legacy.WasOverdue);
        Assert.Equal(LeadTimeStatus.UntrackedLegacy, TestTiming.Value.Status(legacy, now.AddYears(1)));
    }
    [Theory]
    [InlineData("utc-start")]
    [InlineData("utc-deadline")]
    [InlineData("equal")]
    [InlineData("before")]
    [InlineData("started")]
    [InlineData("completed")]
    public void Start_RejectsInvalidInstantsOrPriorState(string mode)
    {
        var task = NewTask(); var start = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc); var deadline = start.AddDays(5);
        if (mode == "utc-start") start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        if (mode == "utc-deadline") deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Local);
        if (mode == "equal") deadline = start;
        if (mode == "before") deadline = start.AddTicks(-1);
        if (mode == "started") typeof(ProjectTask).GetProperty(nameof(ProjectTask.StartedAt))!.SetValue(task, start);
        if (mode == "completed") typeof(ProjectTask).GetProperty(nameof(ProjectTask.CompletedAt))!.SetValue(task, start);
        Assert.ThrowsAny<Exception>(() => task.Start(start, deadline));
        Assert.Equal(ProjectTaskStatus.Pending, task.Status); Assert.Null(task.DeadlineAt);
    }
    private static ProjectTask NewTask() => new(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "synthetic");
}
