using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfProjectNotificationSweepStore(SnGControlDbContext context, EfProjectNotificationWriter writer) : IProjectNotificationSweepStore
{
    public async Task SweepAsync(DateTime now, CancellationToken token)
    {
        Guid? cursor = null;
        while (true)
        {
            var candidates = context.ProjectTasks.AsNoTracking().Where(t =>
                ((t.Status == ProjectTaskStatus.Pending || t.Status == ProjectTaskStatus.InProgress) &&
                    ((t.Status == ProjectTaskStatus.InProgress && t.DeadlineAt != null) ||
                     (t.AssignedToGoogleSubject != null && !context.ProjectNotifications.Any(n => n.ProjectTaskId == t.Id && n.Kind == NotificationKind.TaskAssigned && n.RecipientGoogleSubject == t.AssignedToGoogleSubject && n.ScheduledAt == t.AssignedAt)))) ||
                (t.Status == ProjectTaskStatus.Completed && t.DeadlineAt != null && t.AssignedToGoogleSubject != null &&
                    ((t.CompletedAt > t.DeadlineAt && !context.ProjectNotifications.Any(n => n.ProjectTaskId == t.Id && n.Kind == NotificationKind.Overdue)) ||
                     context.ProjectNotifications.Any(n => n.ProjectTaskId == t.Id && n.Kind == NotificationKind.Overdue && n.State == NotificationState.Active))));
            if (cursor is Guid after) candidates = candidates.Where(t => t.Id.CompareTo(after) > 0);
            var batch = await candidates.OrderBy(t => t.Id).Select(t => new { t.Id, t.ProjectId }).Take(200).ToArrayAsync(token);
            if (batch.Length == 0) return;
            cursor = batch[^1].Id;
            var ids = batch.Select(t => t.Id).ToArray(); var projects = batch.Select(t => t.ProjectId).Distinct().ToArray();
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            // Same lock order as workflow transitions: Project, then tasks, then notifications.
            await context.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = ANY({projects}) ORDER BY \"Id\" FOR UPDATE").AsNoTracking().ToArrayAsync(token);
            var tasks = await context.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(token);
            var before = tasks.Select(t => (Task: t, Values: context.Entry(t).CurrentValues.Clone())).ToArray();
            EfProjectNotificationWriter.NotificationWriteSet? changes = null;
            try
            {
                changes = await writer.StageAsync(tasks, now, token);
                await context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (DbUpdateException error) when (IsDuplicateEvent(error))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                changes?.RestoreTracking();
                foreach (var (task, values) in before) context.Entry(task).CurrentValues.SetValues(values);
            }
            finally
            {
                foreach (var entry in context.ChangeTracker.Entries().Where(e =>
                    e.Entity is SnG.Control.Domain.Entities.ProjectTask t && ids.Contains(t.Id) ||
                    e.Entity is SnG.Control.Domain.Entities.ProjectNotification n && ids.Contains(n.ProjectTaskId)).ToArray()) entry.State = EntityState.Detached;
            }
        }
    }
    internal static bool IsDuplicateEvent(DbUpdateException error) => error.InnerException is PostgresException
    { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ProjectNotificationConfiguration.EventKeyIndex };
}
