using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Notifications;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfProjectNotificationStore(SnGControlDbContext context) : IProjectNotificationStore
{
    internal IQueryable<ProjectNotificationResponse> PersonalQuery(string subject) =>
        from n in context.ProjectNotifications.AsNoTracking()
        join t in context.ProjectTasks.AsNoTracking() on n.ProjectTaskId equals t.Id
        where n.RecipientGoogleSubject == subject
        orderby n.CreatedAt descending, n.Id descending
        select new ProjectNotificationResponse(n.Id, n.ProjectId, n.ProjectTaskId, n.RecipientGoogleSubject, t.Type,
            n.Kind, n.State, n.Title, n.Message, n.ScheduledAt, n.CreatedAt, n.ResolvedAt, t.DeadlineAt);
    internal IQueryable<ProjectNotificationResponse> ProjectQuery(Guid projectId) =>
        from n in context.ProjectNotifications.AsNoTracking()
        join t in context.ProjectTasks.AsNoTracking() on n.ProjectTaskId equals t.Id
        where n.ProjectId == projectId
        orderby n.CreatedAt, n.Id
        select new ProjectNotificationResponse(n.Id, n.ProjectId, n.ProjectTaskId, n.RecipientGoogleSubject, t.Type,
            n.Kind, n.State, n.Title, n.Message, n.ScheduledAt, n.CreatedAt, n.ResolvedAt, t.DeadlineAt);
    public async Task<IReadOnlyList<ProjectNotificationResponse>> ListPersonalAsync(string subject, CancellationToken token) =>
        await PersonalQuery(subject).ToArrayAsync(token);
    public async Task<IReadOnlyList<ProjectNotificationResponse>?> ListProjectAsync(Guid projectId, CancellationToken token) =>
        !await context.Projects.AnyAsync(p => p.Id == projectId, token) ? null :
        await ProjectQuery(projectId).ToArrayAsync(token);
}
