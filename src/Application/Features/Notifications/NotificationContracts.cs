using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Notifications;

public sealed record ProjectNotificationResponse(Guid Id, Guid ProjectId, Guid ProjectTaskId, string RecipientGoogleSubject,
    ProjectTaskType TaskType, NotificationKind Kind, NotificationState State, string Title, string Message,
    DateTime ScheduledAt, DateTime CreatedAt, DateTime? ResolvedAt, DateTime? DeadlineAt);

public interface IProjectNotificationStore
{
    Task<IReadOnlyList<ProjectNotificationResponse>> ListPersonalAsync(string subject, CancellationToken token);
    Task<IReadOnlyList<ProjectNotificationResponse>?> ListProjectAsync(Guid projectId, CancellationToken token);
}
public interface IProjectNotificationSweepStore
{
    Task SweepAsync(DateTime now, CancellationToken token);
}
