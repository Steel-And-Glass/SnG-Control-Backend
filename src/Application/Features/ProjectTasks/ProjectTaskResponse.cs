using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.ProjectTasks;

public sealed record ProjectTaskResponse(
    Guid Id, Guid ProjectId, OperationalArea Area, ProjectTaskType Type, ProjectTaskStatus Status,
    DateTime AssignedAt, DateTime? StartedAt, DateTime? CompletedAt, string? AssignedToGoogleSubject, DateTime? DeadlineAt, bool? WasOverdue, LeadTimeStatus LeadTimeStatus, int? QuotationRound = 1)
{
    public static ProjectTaskResponse FromEntity(ProjectTask task, TaskTiming timing) => new(
        task.Id, task.ProjectId, task.Area, task.Type, task.Status,
        task.AssignedAt, task.StartedAt, task.CompletedAt, task.AssignedToGoogleSubject, task.DeadlineAt, task.WasOverdue, timing.Status(task, timing.UtcNow), task.QuotationRound);
}
