using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfClientQuotationDecisionStore(SnGControlDbContext context, TaskTiming timing,
    EfProjectNotificationWriter notifications) : IClientQuotationDecisionStore
{
    public async Task<DecisionResult> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        if (!await context.Projects.AsNoTracking().Where(scope.Filter(context.ProjectTasks)).AnyAsync(p => p.Id == id, token))
            return new(DecisionStatus.ProjectNotFound);
        var quotation = await context.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == id, token);
        var round = quotation is null ? (int?)null : await context.BudgetQuotationVersions.Where(v => v.Id == quotation.BudgetQuotationVersionId).Select(v => v.VersionNumber).SingleAsync(token);
        var value = quotation is null ? null : await context.ClientQuotationDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.CommercialQuotationId == quotation.Id, token);
        return new(DecisionStatus.Success, DecisionResponse.From(id, value, round));
    }
    public async Task<DecisionResult> RecordAsync(Guid id, ClientQuotationDecisionType decision, string? note, DecisionActor actor, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var project = await context.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token);
        if (project is null) return new(DecisionStatus.ProjectNotFound);
        var tasks = await context.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").ToArrayAsync(token);
        var quotation = await context.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == id, token);
        if (quotation is null) return new(DecisionStatus.QuotationUnavailable);
        var round = await context.BudgetQuotationVersions.Where(v => v.Id == quotation.BudgetQuotationVersionId).Select(v => v.VersionNumber).SingleAsync(token);
        // Ownership is checked before revealing whether another request already recorded a decision.
        var presentations = tasks.Where(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == round).ToArray();
        if (presentations.Length > 1) return new(DecisionStatus.InvalidWorkflowState);
        var presentation = presentations.SingleOrDefault();
        if (presentation is not null && presentation.AssignedToGoogleSubject != actor.Subject) return new(DecisionStatus.Forbidden);
        if (await context.ClientQuotationDecisions.AnyAsync(d => d.CommercialQuotationId == quotation.Id, token)) return new(DecisionStatus.AlreadyRecorded);
        var status = DecisionWorkflow.Validate(id, quotation, tasks, actor.Subject, round);
        if (status != DecisionStatus.Success) return new(status);
        var task = presentation!;
        var instant = timing.UtcNow;
        if (instant < task.StartedAt) return new(DecisionStatus.InvalidWorkflowState);
        // Round upward to PostgreSQL microseconds so a late completion cannot serialize as on-time.
        var now = new DateTime(instant.Ticks + (10 - instant.Ticks % 10) % 10, DateTimeKind.Utc);
        var record = new ClientQuotationDecision(id, quotation!.Id, task.Id, decision, note, now, actor.Subject, actor.Email, actor.Name);
        var before = context.Entry(task).CurrentValues.Clone();
        EfProjectNotificationWriter.NotificationWriteSet? changes = null;
        try
        {
            task.Complete(now); context.Add(record);
            changes = await notifications.StageAsync([task], now, token);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(DecisionStatus.Success, Recorded: new(DecisionResponse.From(id, record, round), ProjectTaskResponse.FromEntity(task, timing)));
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            changes?.RestoreTracking();
            context.Entry(task).CurrentValues.SetValues(before);
            context.Entry(task).State = EntityState.Detached;
            context.Entry(record).State = EntityState.Detached;
            if (failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }
                && pg.ConstraintName is ClientQuotationDecisionConfiguration.QuotationIndex or ClientQuotationDecisionConfiguration.TaskIndex)
                return new(DecisionStatus.AlreadyRecorded);
            throw;
        }
    }
}
