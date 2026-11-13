using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Management;

public sealed class GetManagementOverviewUseCase(IManagementReadStore store, ListManagementTasksUseCase list)
{
    public const int AttentionItemLimit = 10;

    public async Task<ManagementOverviewResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var totalProjects = await store.CountProjectsAsync(cancellationToken);
        var tasks = await list.ExecuteAsync(cancellationToken);
        int Count(LeadTimeStatus status) => tasks.Count(task => task.LeadTimeStatus == status);
        var attention = tasks.Where(task => task.LeadTimeStatus is LeadTimeStatus.Overdue
            or LeadTimeStatus.DueToday or LeadTimeStatus.DueSoon).ToArray();
        // The task list already orders attention by priority, deadline, ordinal project name and task id.
        return new(totalProjects,
            new(tasks.Count(t => t.Status == ProjectTaskStatus.Pending),
                tasks.Count(t => t.Status == ProjectTaskStatus.InProgress), tasks.Count(t => t.Status == ProjectTaskStatus.Completed)),
            new(Count(LeadTimeStatus.NotStarted), Count(LeadTimeStatus.OnTrack), Count(LeadTimeStatus.DueSoon),
                Count(LeadTimeStatus.DueToday), Count(LeadTimeStatus.Overdue), Count(LeadTimeStatus.CompletedOnTime),
                Count(LeadTimeStatus.CompletedLate), Count(LeadTimeStatus.UntrackedLegacy)),
            tasks.GroupBy(t => t.Area).OrderBy(group => group.Key).Select(group => new ManagementAreaSummary(group.Key,
                group.Count(t => t.Status == ProjectTaskStatus.Pending), group.Count(t => t.Status == ProjectTaskStatus.InProgress),
                group.Count(t => t.Status == ProjectTaskStatus.Completed), group.Count(t => t.LeadTimeStatus == LeadTimeStatus.Overdue),
                group.Count(t => t.LeadTimeStatus == LeadTimeStatus.DueToday), group.Count(t => t.LeadTimeStatus == LeadTimeStatus.DueSoon))).ToArray(),
            new(attention.Length, attention.Take(AttentionItemLimit).Select(t => new ManagementAttentionItem(t.TaskId,
                t.ProjectId, t.ProjectName, t.ClientName, t.SgCode, t.Area, t.Type, t.Status, t.LeadTimeStatus, t.DeadlineAt, t.StartedAt)).ToArray()));
    }
}
