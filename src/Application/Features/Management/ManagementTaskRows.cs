using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Management;

// Single projection of management task rows: backend-owned Lead Time status and operational ordering.
public static class ManagementTaskRows
{
    public static IReadOnlyList<ManagementTaskResponse> Project(IEnumerable<ManagementTaskData> data, TaskTiming timing)
    {
        var now = timing.UtcNow;
        return data.Select(row =>
        {
            var task = row.Task;
            return new ManagementTaskResponse(task.Id, task.ProjectId, row.ProjectName, row.ClientId,
                row.ClientName, row.SgCode, row.City, row.ProductLine, task.Area, task.Type, task.Status,
                task.AssignedAt, task.StartedAt, task.CompletedAt, task.DeadlineAt, task.WasOverdue, timing.Status(task, now), task.QuotationRound);
        }).OrderBy(row => row.Status == ProjectTaskStatus.Completed)
            .ThenBy(row => Priority(row.LeadTimeStatus))
            .ThenBy(row => row.Status == ProjectTaskStatus.Completed ? (DateTime?)null : row.DeadlineAt ?? DateTime.MaxValue)
            .ThenByDescending(row => row.Status == ProjectTaskStatus.Completed ? row.CompletedAt : null)
            .ThenBy(row => row.ProjectName, StringComparer.Ordinal)
            .ThenBy(row => row.TaskId).ToArray();
    }

    private static int Priority(LeadTimeStatus status) => status switch
    {
        LeadTimeStatus.Overdue => 0,
        LeadTimeStatus.DueToday => 1,
        LeadTimeStatus.DueSoon => 2,
        LeadTimeStatus.OnTrack => 3,
        LeadTimeStatus.NotStarted => 4,
        LeadTimeStatus.CompletedLate => 0,
        LeadTimeStatus.CompletedOnTime => 1,
        _ => 5 // UntrackedLegacy, including completed tasks without historical timing.
    };
}
