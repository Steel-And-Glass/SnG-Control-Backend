using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Features.Requotations;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence.Configurations;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfRequotationStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications) : IRequotationStore
{
    public async Task<RequotationResult> RequestAsync(Guid id, string changes, DecisionActor actor, string budgetsSubject, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token);
        if (project is null) return new(RequotationStatus.ProjectNotFound);
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var quotation = await db.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == id, token);
        if (quotation is null) return new(RequotationStatus.ClientDecisionRequired);
        var round = await db.BudgetQuotationVersions.Where(v => v.Id == quotation.BudgetQuotationVersionId && v.ProjectId == id).Select(v => v.VersionNumber).SingleAsync(token);
        var presentations = tasks.Where(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == round).ToArray();
        if (presentations.Length != 1) return new(RequotationStatus.InvalidWorkflowState);
        var presentation = presentations[0];
        if (presentation.AssignedToGoogleSubject != actor.Subject) return new(RequotationStatus.Forbidden);
        var decision = await db.ClientQuotationDecisions.FromSqlInterpolated($"SELECT * FROM \"ClientQuotationDecisions\" WHERE \"CommercialQuotationId\" = {quotation.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token);
        if (decision is null) return new(RequotationStatus.ClientDecisionRequired);
        if (decision.Decision != ClientQuotationDecisionType.ChangesRequested) return new(RequotationStatus.DecisionDoesNotRequestChanges);
        if (await db.RequotationRequests.AnyAsync(r => r.SourceClientQuotationDecisionId == decision.Id, token)) return new(RequotationStatus.AlreadyRequested);
        if (decision.ProjectId != id || decision.PresentQuotationTaskId != presentation.Id || presentation.Area != OperationalArea.Commercial
            || presentation.Status != ProjectTaskStatus.Completed || presentation.CompletedAt is null || presentation.StartedAt is null
            || project.SgCode is null || tasks.Any(t => t.QuotationRound > round || t.Status != ProjectTaskStatus.Completed)
            || await db.BudgetQuotationVersions.AnyAsync(v => v.ProjectId == id && v.VersionNumber > round, token)) return new(RequotationStatus.InvalidWorkflowState);
        var target = checked(round + 1);
        var instant = timing.UtcNow;
        var now = new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
        var task = new ProjectTask(id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, budgetsSubject, now, target);
        timing.Start(task, now);
        var request = new RequotationRequest(id, decision.Id, target, task.Id, changes, now, actor.Subject, actor.Email, actor.Name);
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            db.AddRange(task, request); staged = await notifications.StageAsync([task], now, token);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            return new(RequotationStatus.Success, new(new(id, decision.Id, target, request.RequestedChanges, now), ProjectTaskResponse.FromEntity(task, timing)));
        }
        catch (Exception failure)
        {
            await transaction.RollbackAsync(CancellationToken.None); staged?.RestoreTracking();
            db.Entry(task).State = EntityState.Detached; db.Entry(request).State = EntityState.Detached;
            if (failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }
                && pg.ConstraintName is RequotationRequestConfiguration.DecisionIndex or RequotationRequestConfiguration.TaskIndex or RequotationRequestConfiguration.RoundIndex
                or ProjectTaskConfiguration.PrepareQuotationIndexName) return new(RequotationStatus.AlreadyRequested);
            throw;
        }
    }
    public async Task<QuotationRoundsResponse?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await db.Projects.Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token)) return null;
        var numbers = db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id && t.QuotationRound != null)
            .Select(t => t.QuotationRound!.Value).Union(db.BudgetQuotationVersions.Where(v => v.ProjectId == id).Select(v => v.VersionNumber));
        var total = await numbers.CountAsync(token); var selected = await numbers.Order().Take(20).ToArrayAsync(token);
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id && selected.Contains(t.QuotationRound!.Value)).ToArrayAsync(token);
        var budgets = await db.BudgetQuotationVersions.AsNoTracking().Where(v => v.ProjectId == id && selected.Contains(v.VersionNumber))
            .Select(v => new RoundBudget(v.VersionNumber, v.ConfirmedAt,
                db.CommercialCatalogItems.Count(i => db.CommercialCatalogs.Any(c => c.Id == i.CommercialCatalogId && c.BudgetQuotationVersionId == v.Id)),
                db.CommercialCatalogItems.Where(i => db.CommercialCatalogs.Any(c => c.Id == i.CommercialCatalogId && c.BudgetQuotationVersionId == v.Id)).Sum(i => (long?)i.Quantity) ?? 0)).ToArrayAsync(token);
        var commercial = await (from q in db.CommercialQuotations.AsNoTracking()
                                join v in db.BudgetQuotationVersions.AsNoTracking() on q.BudgetQuotationVersionId equals v.Id
                                where q.ProjectId == id && selected.Contains(v.VersionNumber)
                                select new { q.Id, Round = v.VersionNumber, q.ConfirmedAt, q.ProjectTotal }).ToArrayAsync(token);
        var quoteIds = commercial.Select(q => q.Id).ToArray();
        var decisions = await db.ClientQuotationDecisions.AsNoTracking().Where(d => quoteIds.Contains(d.CommercialQuotationId)).ToArrayAsync(token);
        var requests = await db.RequotationRequests.AsNoTracking().Where(r => r.ProjectId == id && selected.Contains(r.TargetQuotationRound)).ToArrayAsync(token);
        ProjectTaskResponse? Task(int round, ProjectTaskType type) => tasks.SingleOrDefault(t => t.QuotationRound == round && t.Type == type) is { } task ? ProjectTaskResponse.FromEntity(task, timing) : null;
        var rounds = selected.Select(round =>
        {
            var q = commercial.SingleOrDefault(q => q.Round == round); var d = decisions.SingleOrDefault(d => d.CommercialQuotationId == q?.Id);
            var r = requests.SingleOrDefault(r => r.TargetQuotationRound == round);
            return new QuotationRoundSummary(round, Task(round, ProjectTaskType.PrepareQuotation), budgets.SingleOrDefault(b => b.VersionNumber == round),
                Task(round, ProjectTaskType.ReviewQuotation), q is null ? null : new(round, q.ConfirmedAt, q.ProjectTotal, Money.CurrencyCode),
                Task(round, ProjectTaskType.PresentQuotationToClient), d is null ? null : new(d.Decision, d.Note, d.RecordedAt),
                r is null ? null : new(r.TargetQuotationRound, r.RequestedChanges, r.RequestedAt));
        }).ToArray();
        await transaction.CommitAsync(token); return new(id, total, rounds.Length, total > rounds.Length, rounds);
    }
}
