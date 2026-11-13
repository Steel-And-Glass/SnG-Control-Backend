using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Stores;

// Stages notification changes in the caller's existing task/file transaction. Never saves or commits.
public class EfProjectNotificationWriter(SnGControlDbContext context, ProjectNotificationPlanner planner)
{
    protected virtual Task<List<ProjectNotification>> LoadAsync(Guid[] ids, CancellationToken token) =>
        context.ProjectNotifications.Where(n => ids.Contains(n.ProjectTaskId)).ToListAsync(token);

    public async Task<NotificationWriteSet> StageAsync(IReadOnlyList<ProjectTask> tasks, DateTime now, CancellationToken token)
    {
        var persistedIds = tasks.Where(t => context.Entry(t).State != EntityState.Added).Select(t => t.Id).ToArray();
        var existing = persistedIds.Length == 0 ? [] : await LoadAsync(persistedIds, token);
        var before = existing.Select(n => (n, context.Entry(n).CurrentValues.Clone(), context.Entry(n).OriginalValues.Clone(), context.Entry(n).State)).ToArray();
        var added = tasks.SelectMany(t => planner.Reconcile(t, existing, now)).ToArray();
        context.AddRange(added);
        return new(context, before, added);
    }

    public sealed class NotificationWriteSet(SnGControlDbContext context,
        (ProjectNotification Entity, PropertyValues Values, PropertyValues Originals, EntityState State)[] before, ProjectNotification[] added)
    {
        public void RestoreTracking()
        {
            foreach (var notification in added) context.Entry(notification).State = EntityState.Detached;
            foreach (var (entity, values, originals, state) in before)
            {
                context.Entry(entity).OriginalValues.SetValues(originals);
                context.Entry(entity).CurrentValues.SetValues(values);
                context.Entry(entity).State = state;
                context.Entry(entity).CurrentValues.SetValues(values);
            }
        }
    }
}
