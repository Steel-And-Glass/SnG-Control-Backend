using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SnG.Control.Application.Features.Budgets;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfBudgetsWorkflowStore(SnGControlDbContext context) : IBudgetsWorkflowStore, IAsyncDisposable
{
    private IDbContextTransaction? transaction;

    public async Task<(Project? Project, ProjectTask? PrepareQuotationTask, bool ValidState)> LoadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var project = await context.Projects
            .FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {projectId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var tasks = await context.ProjectTasks.Where(t => t.ProjectId == projectId && t.Type == ProjectTaskType.PrepareQuotation).ToArrayAsync(cancellationToken);
        var active = tasks.Where(t => t.Status != ProjectTaskStatus.Completed).ToArray();
        var task = active.Length == 1 ? active[0] : active.Length == 0 ? tasks.OrderByDescending(t => t.QuotationRound).FirstOrDefault() : null;
        return (project, task, active.Length <= 1);
    }

    public async Task<bool> TrySaveAsync(Project project, ProjectTask task, CancellationToken cancellationToken)
    {
        // PostgreSQL timestamps have microsecond precision; return exactly what is stored.
        context.ChangeTracker.DetectChanges();
        var updatedAt = context.Entry(project).Property(project => project.UpdatedAt);
        if (updatedAt.IsModified) updatedAt.CurrentValue = ToMicroseconds(updatedAt.CurrentValue);
        var startedAt = context.Entry(task).Property(task => task.StartedAt);
        if (startedAt.IsModified && startedAt.CurrentValue is DateTime started)
            startedAt.CurrentValue = ToMicroseconds(started);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectConfiguration.SgCodeIndexName
        })
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            context.Entry(project).State = EntityState.Detached;
            context.Entry(task).State = EntityState.Detached;
            return false;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is null) return;
        await transaction.DisposeAsync();
        transaction = null;
    }

    private static DateTime ToMicroseconds(DateTime value) => new(value.Ticks - value.Ticks % 10, value.Kind);
}
