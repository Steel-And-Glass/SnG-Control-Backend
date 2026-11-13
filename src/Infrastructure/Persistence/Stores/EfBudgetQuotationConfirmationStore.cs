using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SnG.Control.Application.Features.Budgets;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfBudgetQuotationConfirmationStore(SnGControlDbContext context, IFileStorage storage) : IBudgetQuotationConfirmationStore
{
    public async Task<QuotationConfirmationStoreResult> ConfirmAsync(BudgetQuotationConfirmation confirmation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // SHARE blocks concurrent SG corrections (FOR UPDATE), while two confirmations can reach the unique index.
        // Parsing has already finished; the task is only read and is never locked or modified.
        var project = await context.Projects.FromSqlInterpolated(
            $"SELECT * FROM \"Projects\" WHERE \"Id\" = {confirmation.Version.ProjectId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (project is null) return new(QuotationConfirmationStatus.ProjectNotFound);
        var task = BudgetQuotationWorkflow.ReadyTask(project, await context.ProjectTasks.AsNoTracking()
            .Where(value => value.ProjectId == project.Id).ToArrayAsync(cancellationToken));
        if (task is null || task.QuotationRound != confirmation.Version.VersionNumber) return new(QuotationConfirmationStatus.InvalidWorkflowState);
        if (!string.Equals(project.SgCode, confirmation.Version.DocumentSgCode, StringComparison.Ordinal))
            return new(QuotationConfirmationStatus.SgCodeMismatch);
        if (await context.BudgetQuotationVersions.AnyAsync(value => value.ProjectId == project.Id && value.VersionNumber == confirmation.Version.VersionNumber, cancellationToken))
            return new(QuotationConfirmationStatus.AlreadyConfirmed);
        return await PersistAsync(confirmation, task, transaction, cancellationToken);
    }

    internal async Task<QuotationConfirmationStoreResult> PersistAsync(BudgetQuotationConfirmation confirmation, ProjectTask task,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var createdKeys = new List<string>();
        var commitStarted = false;
        var entities = new object[] { confirmation.Version, confirmation.Catalog }
            .Concat(confirmation.Items).Concat(confirmation.Extractions).ToArray();
        try
        {
            context.AddRange(entities);
            await context.SaveChangesAsync(cancellationToken);
            foreach (var file in confirmation.Files)
            {
                await storage.SaveAsync(file.StorageKey, file.Bytes, cancellationToken);
                createdKeys.Add(file.StorageKey);
            }
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
            return new(QuotationConfirmationStatus.Created, task);
        }
        catch (Exception failure)
        {
            var compensationFailures = new List<Exception>();
            var rolledBack = false;
            try { await transaction.RollbackAsync(CancellationToken.None); rolledBack = true; }
            catch (Exception rollbackFailure) { compensationFailures.Add(rollbackFailure); }
            // An unacknowledged COMMIT may have succeeded. Preserve files if rollback cannot establish failure.
            if (!commitStarted || rolledBack)
                foreach (var key in createdKeys.AsEnumerable().Reverse())
                    try { await storage.DeleteAsync(key, CancellationToken.None); }
                    catch (Exception cleanupFailure) { compensationFailures.Add(cleanupFailure); }
            foreach (var entity in entities) context.Entry(entity).State = EntityState.Detached;
            if (compensationFailures.Count > 0)
                throw new AggregateException("Quotation confirmation failed; compensation requires inspection.", new[] { failure }.Concat(compensationFailures));
            if (failure is DbUpdateException
                {
                    InnerException: PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BudgetQuotationVersionConfiguration.VersionIndexName }
                })
                return new(QuotationConfirmationStatus.AlreadyConfirmed);
            if (failure is OperationCanceledException) throw;
            throw new InvalidOperationException("Quotation confirmation could not be stored.", failure);
        }
    }
}
