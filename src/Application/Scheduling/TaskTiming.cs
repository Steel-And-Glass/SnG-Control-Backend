using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Scheduling;

public enum LeadTimeStatus { NotStarted, OnTrack, DueSoon, DueToday, Overdue, CompletedOnTime, CompletedLate, UntrackedLegacy }

public sealed class TaskTiming(TimeProvider clock, ColombiaBusinessCalendar calendar)
{
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    public static int BusinessDays(ProjectTaskType type) => type switch
    {
        ProjectTaskType.PrepareQuotation => 5,
        ProjectTaskType.ReviewQuotation => 5,
        ProjectTaskType.PresentQuotationToClient => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "No Lead Time policy exists for this task type.")
    };
    public DateTime Deadline(ProjectTaskType type, DateTime startedAt) => calendar.AtLocalTime(calendar.MoveBusinessDays(calendar.LocalDate(startedAt), BusinessDays(type)), 17);
    public DateTime DueSoon(DateTime deadline) => calendar.AtLocalTime(calendar.MoveBusinessDays(calendar.LocalDate(deadline), -2), 8);
    public DateTime DueToday(DateTime deadline) => calendar.AtLocalTime(calendar.LocalDate(deadline), 8);
    public void Start(ProjectTask task, DateTime now) => task.Start(now, Deadline(task.Type, now));
    public LeadTimeStatus Status(ProjectTask task, DateTime now)
    {
        if (task.Status == ProjectTaskStatus.Pending && task.DeadlineAt is null) return LeadTimeStatus.NotStarted;
        if (task.DeadlineAt is not DateTime deadline) return LeadTimeStatus.UntrackedLegacy;
        if (task.Status == ProjectTaskStatus.Completed) return task.WasOverdue switch
        { true => LeadTimeStatus.CompletedLate, false => LeadTimeStatus.CompletedOnTime, _ => LeadTimeStatus.UntrackedLegacy };
        if (now > deadline) return LeadTimeStatus.Overdue;
        if (calendar.LocalDate(now) == calendar.LocalDate(deadline)) return LeadTimeStatus.DueToday;
        return now >= DueSoon(deadline) ? LeadTimeStatus.DueSoon : LeadTimeStatus.OnTrack;
    }
}
