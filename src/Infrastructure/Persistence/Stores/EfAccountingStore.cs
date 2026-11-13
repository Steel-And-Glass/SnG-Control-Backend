using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfAccountingStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications) : IAccountingStore
{
    private static DateTime Microseconds(DateTime instant) => new(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
    public async Task<AccountingState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await db.Projects.Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token)) return null;
        var h = await db.AccountingHandoffs.AsNoTracking().Where(h => h.ProjectId == id).OrderByDescending(h => h.QuotationRound).FirstOrDefaultAsync(token);
        var d = h is null ? null : await db.InitialDeposits.AsNoTracking().SingleOrDefaultAsync(d => d.AccountingHandoffId == h.Id, token);
        await transaction.CommitAsync(token); return AccountingState.From(id, h, d);
    }
    public async Task<AccountingResult> SendAsync(Guid id, DecisionActor actor, string accountantSubject, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token);
        if (project is null) return new(AccountingStatus.ProjectNotFound);
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var current = await db.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == id, token);
        if (current is null) return new(AccountingStatus.ClientDecisionRequired);
        var quotation = await db.CommercialQuotations.FromSqlInterpolated($"SELECT * FROM \"CommercialQuotations\" WHERE \"Id\" = {current.Id} FOR UPDATE").AsNoTracking().SingleAsync(token);
        var round = await db.BudgetQuotationVersions.Where(v => v.Id == quotation.BudgetQuotationVersionId && v.ProjectId == id).Select(v => v.VersionNumber).SingleAsync(token);
        var presentations = tasks.Where(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == round).ToArray();
        if (presentations.Length != 1) return new(AccountingStatus.InvalidWorkflowState);
        var presentation = presentations[0];
        if (presentation.AssignedToGoogleSubject != actor.Subject) return new(AccountingStatus.Forbidden);
        var decision = await db.ClientQuotationDecisions.FromSqlInterpolated($"SELECT * FROM \"ClientQuotationDecisions\" WHERE \"CommercialQuotationId\" = {quotation.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token);
        if (decision is null) return new(AccountingStatus.ClientDecisionRequired);
        if (decision.Decision != ClientQuotationDecisionType.Approved) return new(AccountingStatus.DecisionNotApproved);
        if (await db.AccountingHandoffs.AnyAsync(h => h.SourceClientQuotationDecisionId == decision.Id || h.CommercialQuotationId == quotation.Id, token)) return new(AccountingStatus.AlreadySentToAccounting);
        if (decision.ProjectId != id || decision.PresentQuotationTaskId != presentation.Id || presentation.Area != OperationalArea.Commercial || presentation.Status != ProjectTaskStatus.Completed
            || presentation.CompletedAt is null || presentation.StartedAt is null || tasks.Any(t => t.QuotationRound > round || t.Type == ProjectTaskType.RegisterInitialDeposit)) return new(AccountingStatus.InvalidWorkflowState);
        var now = Microseconds(timing.UtcNow);
        var task = new ProjectTask(id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, accountantSubject, now);
        var handoff = new AccountingHandoff(id, decision.Id, quotation.Id, round, task.Id, now, actor.Subject, actor.Email, actor.Name);
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            db.AddRange(task, handoff); staged = await notifications.StageAsync([task], now, token);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            return new(AccountingStatus.Success, new(AccountingState.From(id, handoff, null), ProjectTaskResponse.FromEntity(task, timing)));
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(CancellationToken.None); staged?.RestoreTracking(); db.Entry(task).State = EntityState.Detached; db.Entry(handoff).State = EntityState.Detached;
            if (failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }
                && pg.ConstraintName is AccountingHandoffConfiguration.DecisionIndex or AccountingHandoffConfiguration.QuotationIndex or AccountingHandoffConfiguration.TaskIndex) return new(AccountingStatus.AlreadySentToAccounting);
            throw;
        }
    }
    public async Task<AccountingResult> RecordAsync(Guid id, DepositInput input, DecisionActor actor, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token) is null) return new(AccountingStatus.ProjectNotFound);
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").ToArrayAsync(token);
        var handoffs = await db.AccountingHandoffs.FromSqlInterpolated($"SELECT * FROM \"AccountingHandoffs\" WHERE \"ProjectId\" = {id} ORDER BY \"QuotationRound\" DESC FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var h = handoffs.FirstOrDefault(); if (h is null) return new(AccountingStatus.AccountingHandoffRequired);
        var task = tasks.SingleOrDefault(t => t.Id == h.RegisterInitialDepositTaskId);
        if (task is null || task.Type != ProjectTaskType.RegisterInitialDeposit || task.Area != OperationalArea.Accounting || task.QuotationRound is not null) return new(AccountingStatus.InvalidWorkflowState);
        if (task.AssignedToGoogleSubject != actor.Subject) return new(AccountingStatus.Forbidden);
        if (await db.InitialDeposits.AnyAsync(d => d.AccountingHandoffId == h.Id, token)) return new(AccountingStatus.AlreadyRecorded);
        if (task.Status != ProjectTaskStatus.Pending || task.StartedAt is not null || task.CompletedAt is not null || task.DeadlineAt is not null || task.WasOverdue is not null) return new(AccountingStatus.InvalidWorkflowState);
        var now = Microseconds(timing.UtcNow);
        if (input.ReceivedAt > timing.UtcNow) return new(AccountingStatus.InvalidDeposit);
        var deposit = new InitialDeposit(h, input.Amount, Microseconds(input.ReceivedAt), now, input.Reference, input.Note, actor.Subject, actor.Email, actor.Name);
        var before = db.Entry(task).CurrentValues.Clone(); EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            task.StartUntracked(now); task.Complete(now); db.Add(deposit); staged = await notifications.StageAsync([task], now, token);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            return new(AccountingStatus.Success, new(AccountingState.From(id, h, deposit), ProjectTaskResponse.FromEntity(task, timing)));
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(CancellationToken.None); staged?.RestoreTracking(); db.Entry(task).CurrentValues.SetValues(before);
            db.Entry(task).State = EntityState.Unchanged; db.Entry(deposit).State = EntityState.Detached;
            if (failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }
                && pg.ConstraintName is InitialDepositConfiguration.HandoffIndex or InitialDepositConfiguration.TaskIndex or InitialDepositConfiguration.QuotationIndex) return new(AccountingStatus.AlreadyRecorded);
            throw;
        }
    }
}
