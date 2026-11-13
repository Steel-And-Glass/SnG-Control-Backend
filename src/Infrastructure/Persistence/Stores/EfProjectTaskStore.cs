using SnG.Control.Application.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public class EfProjectTaskStore(SnGControlDbContext context, TaskTiming timing, EfProjectNotificationWriter notifications) : IProjectTaskStore
{
    public async Task<bool> TryAddPrepareQuotationAsync(ProjectTask task, CancellationToken cancellationToken)
    {
        await context.ProjectTasks.AddAsync(task, cancellationToken);
        var changes = await notifications.StageAsync([task], timing.UtcNow, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectTaskConfiguration.PrepareQuotationIndexName
        })
        {
            changes.RestoreTracking();
            context.Entry(task).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<ProjectTask>> ListByAreaAsync(
        OperationalArea area, ProjectTaskStatus status, SnG.Control.Application.Access.TaskInboxScope scope, CancellationToken cancellationToken)
    {
        var projects = context.Projects.Where(new SnG.Control.Application.Access.ProjectReadScope(scope.Subject, scope.Role).Filter(context.ProjectTasks));
        return await context.ProjectTasks.AsNoTracking().Where(scope.Filter())
            .Where(task => task.Area == area && task.Status == status
                && projects.Any(p => p.Id == task.ProjectId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await context.ProjectTasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId)
            .ToListAsync(cancellationToken);
}
