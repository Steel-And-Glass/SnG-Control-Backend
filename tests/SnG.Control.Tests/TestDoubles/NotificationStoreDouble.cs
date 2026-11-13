using SnG.Control.Application.Features.Notifications;

namespace SnG.Control.Tests.TestDoubles;

public sealed class NotificationStoreDouble(ProjectStoreDouble projects, ProjectTaskStoreDouble tasks) : IProjectNotificationStore
{
    public List<ProjectNotificationResponse> Extra { get; } = [];
    private IEnumerable<ProjectNotificationResponse> Responses() => tasks.Notifications.Select(n =>
    {
        var task = tasks.Items.Single(t => t.Id == n.ProjectTaskId);
        return new ProjectNotificationResponse(n.Id, n.ProjectId, n.ProjectTaskId, n.RecipientGoogleSubject, task.Type, n.Kind, n.State,
            n.Title, n.Message, n.ScheduledAt, n.CreatedAt, n.ResolvedAt, task.DeadlineAt);
    }).Concat(Extra);
    public Task<IReadOnlyList<ProjectNotificationResponse>> ListPersonalAsync(string subject, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ProjectNotificationResponse>>(Responses().Where(n => n.RecipientGoogleSubject == subject).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToArray());
    public Task<IReadOnlyList<ProjectNotificationResponse>?> ListProjectAsync(Guid id, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ProjectNotificationResponse>?>(projects.Items.Any(p => p.Id == id)
            ? Responses().Where(n => n.ProjectId == id).OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToArray() : null);
}
