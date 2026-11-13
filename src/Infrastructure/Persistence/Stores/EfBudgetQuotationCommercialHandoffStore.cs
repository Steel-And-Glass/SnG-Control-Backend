using SnG.Control.Application.Features.Budgets;
using SnG.Control.Application.Access;
using SnG.Control.Application.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfBudgetQuotationCommercialHandoffStore(SnGControlDbContext context, IFileStorage storage, TaskTiming timing, EfProjectNotificationWriter notifications)
    : IBudgetQuotationCommercialHandoffStore
{
    public Task<CommercialHandoffState?> LoadAsync(Guid projectId, CancellationToken cancellationToken) => ReadAsync(projectId, false, cancellationToken);

    private async Task<CommercialHandoffState?> ReadAsync(Guid projectId, bool locked, CancellationToken token)
    {
        var projects = locked ? context.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {projectId} FOR UPDATE")
            : context.Projects.Where(value => value.Id == projectId);
        var project = await projects.AsNoTracking().SingleOrDefaultAsync(token);
        if (project is null) return null;
        var tasks = locked ? context.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {projectId} FOR UPDATE")
            : context.ProjectTasks.AsNoTracking().Where(value => value.ProjectId == projectId);
        var taskList = await tasks.ToArrayAsync(token);
        var round = BudgetQuotationWorkflow.CurrentRound(taskList, ProjectTaskType.PrepareQuotation);
        var versions = locked ? context.BudgetQuotationVersions.FromSqlInterpolated(
            $"SELECT * FROM \"BudgetQuotationVersions\" WHERE \"ProjectId\" = {projectId} AND \"VersionNumber\" = {round} FOR UPDATE")
            : context.BudgetQuotationVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.VersionNumber == round);
        var version = await versions.SingleOrDefaultAsync(token);
        var catalog = version is null ? null : await context.CommercialCatalogs.AsNoTracking().SingleOrDefaultAsync(value => value.BudgetQuotationVersionId == version.Id, token);
        var items = catalog is null ? [] : await context.CommercialCatalogItems.AsNoTracking().Where(value => value.CommercialCatalogId == catalog.Id).ToArrayAsync(token);
        var ids = items.Select(value => value.Id).ToArray();
        var extractions = await context.CommercialCatalogItemExtractions.AsNoTracking().Where(value => ids.Contains(value.CatalogItemId)).ToArrayAsync(token);
        return new(project, taskList, version, catalog, items, extractions);
    }

    public async Task<CommercialHandoffResult> SendAsync(CommercialHandoffState expected, ReadOnlyMemory<byte> workbook, string commercialHeadSubject, CancellationToken cancellationToken)
    {
        // Generation and package validation have finished before acquiring any database lock.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await ReadAsync(expected.Project.Id, true, cancellationToken);
        if (current is null) return new(CommercialHandoffStatus.ProjectNotFound);
        var status = current.Validate();
        if (status != CommercialHandoffStatus.Sent) return new(status);
        if (!current.SameDocumentAs(expected)) return new(CommercialHandoffStatus.DocumentMismatch);
        return await PersistAsync(current, workbook, transaction, commercialHeadSubject, cancellationToken);
    }

    internal async Task<CommercialHandoffResult> PersistAsync(CommercialHandoffState current, ReadOnlyMemory<byte> workbook,
        IDbContextTransaction transaction, string commercialHeadSubject, CancellationToken cancellationToken)
    {
        var budget = current.Tasks.Single(value => value.Type == ProjectTaskType.PrepareQuotation && value.QuotationRound == current.Version!.VersionNumber);
        var version = current.Version!;
        context.Attach(budget); context.Attach(version);
        var budgetBefore = context.Entry(budget).CurrentValues.Clone();
        var versionBefore = context.Entry(version).CurrentValues.Clone();
        var now = timing.UtcNow;
        EfProjectNotificationWriter.NotificationWriteSet? notificationChanges = null;
        var commercial = new ProjectTask(current.Project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, commercialHeadSubject, now, version.VersionNumber);
        var key = $"projects/{current.Project.Id:D}/budget-quotations/{version.Id:D}/confirmed.xlsx";
        var created = false;
        var commitStarted = false;
        try
        {
            budget.Complete(now); timing.Start(commercial, now); version.SetConfirmedFileStorageKey(key);
            context.Add(commercial);
            // PostgreSQL stores microseconds. Return exactly the timestamps that will be read back.
            TruncateTimestamp(budget, nameof(ProjectTask.CompletedAt));
            TruncateTimestamp(commercial, nameof(ProjectTask.AssignedAt));
            TruncateTimestamp(commercial, nameof(ProjectTask.StartedAt));
            notificationChanges = await notifications.StageAsync([budget, commercial], now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await storage.SaveAsync(key, workbook, cancellationToken);
            created = true;
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
            return new(CommercialHandoffStatus.Sent, new(current.Project.Id, Response(budget), Response(commercial), version.Id, current.Catalog!.Id, true));
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            var rolledBack = false;
            try { await transaction.RollbackAsync(CancellationToken.None); rolledBack = true; }
            catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
            // An unacknowledged commit may have succeeded. Keep its file when rollback cannot establish failure.
            if (created && (!commitStarted || rolledBack))
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception cleanupFailure) { errors.Add(cleanupFailure); }
            try { notificationChanges?.RestoreTracking(); }
            catch (Exception notificationFailure) { errors.Add(notificationFailure); }
            context.Entry(budget).CurrentValues.SetValues(budgetBefore);
            context.Entry(version).CurrentValues.SetValues(versionBefore);
            context.Entry(budget).State = EntityState.Detached;
            context.Entry(version).State = EntityState.Detached;
            context.Entry(commercial).State = EntityState.Detached;
            if (errors.Count > 0) throw new AggregateException("Commercial handoff failed; compensation requires inspection.", new[] { failure }.Concat(errors));
            if (failure is DbUpdateException
                {
                    InnerException: PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ProjectTaskConfiguration.ReviewQuotationIndexName }
                })
                return new(CommercialHandoffStatus.AlreadySent);
            if (failure is OperationCanceledException) throw;
            throw new InvalidOperationException("Commercial handoff could not be stored.", failure);
        }
    }

    public async Task<CommercialFileState> GetFileAsync(Guid projectId, ProjectReadScope scope, CancellationToken cancellationToken)
    {
        if (!await context.Projects.Where(scope.Filter(context.ProjectTasks)).AnyAsync(value => value.Id == projectId, cancellationToken)) return new(false);
        var version = await context.BudgetQuotationVersions.AsNoTracking().Where(value => value.ProjectId == projectId).OrderByDescending(value => value.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        var review = await context.ProjectTasks.AnyAsync(value => value.ProjectId == projectId && value.Area == OperationalArea.Commercial
            && value.Type == ProjectTaskType.ReviewQuotation && value.QuotationRound == (version == null ? 0 : version.VersionNumber), cancellationToken);
        return new(true, version?.ConfirmedFileStorageKey, version?.OriginalFileName, review);
    }

    private void TruncateTimestamp(ProjectTask task, string property)
    {
        var value = (DateTime)context.Entry(task).Property(property).CurrentValue!;
        var ticks = property == nameof(ProjectTask.CompletedAt) && task.DeadlineAt is DateTime deadline && value > deadline
            ? value.Ticks + (10 - value.Ticks % 10) % 10 : value.Ticks - value.Ticks % 10;
        context.Entry(task).Property(property).CurrentValue = new DateTime(ticks, DateTimeKind.Utc);
    }

    private ProjectTaskResponse Response(ProjectTask task) => ProjectTaskResponse.FromEntity(task, timing);
}
