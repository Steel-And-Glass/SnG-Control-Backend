using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Requotations;

public enum RequotationStatus { Success, Forbidden, ProjectNotFound, InvalidRequest, InvalidWorkflowState, ClientDecisionRequired, DecisionDoesNotRequestChanges, AlreadyRequested }
public sealed record RequotationResponse(Guid ProjectId, Guid SourceDecisionId, int TargetQuotationRound, string RequestedChanges, DateTime RequestedAt);
public sealed record RequestedRequotation(RequotationResponse Request, ProjectTaskResponse PrepareQuotationTask);
public sealed record RequotationResult(RequotationStatus Status, RequestedRequotation? Response = null);
public sealed record RoundBudget(int VersionNumber, DateTime ConfirmedAt, int ItemCount, long TotalQuantity);
public sealed record RoundCommercial(int VersionNumber, DateTime ConfirmedAt, decimal ProjectTotal, string CurrencyCode);
public sealed record RoundDecision(ClientQuotationDecisionType Decision, string? Note, DateTime RecordedAt);
public sealed record RoundRequest(int TargetQuotationRound, string RequestedChanges, DateTime RequestedAt);
public sealed record QuotationRoundSummary(int RoundNumber, ProjectTaskResponse? PrepareQuotationTask, RoundBudget? BudgetQuotation,
    ProjectTaskResponse? ReviewQuotationTask, RoundCommercial? CommercialQuotation, ProjectTaskResponse? PresentQuotationTask,
    RoundDecision? ClientDecision, RoundRequest? RequotationRequest);
public sealed record QuotationRoundsResponse(Guid ProjectId, int Total, int Returned, bool HasMore, IReadOnlyList<QuotationRoundSummary> Rounds);
public interface IRequotationStore
{
    Task<RequotationResult> RequestAsync(Guid projectId, string changes, DecisionActor actor, string budgetsSubject, CancellationToken token);
    Task<QuotationRoundsResponse?> ReadAsync(Guid projectId, ProjectReadScope scope, CancellationToken token);
}
public sealed class RequotationService(IRequotationStore store, ICurrentUser user, IActiveRoleContext active, IBudgetsAssigneeIdentity budgets)
{
    public Task<QuotationRoundsResponse?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, new(user.Subject, active.Role), token);
    public Task<RequotationResult> RequestAsync(Guid id, string changes, CancellationToken token)
    {
        if (active.Role != SystemRole.CommercialAdvisor) return Task.FromResult(new RequotationResult(RequotationStatus.Forbidden));
        if (string.IsNullOrWhiteSpace(changes) || changes.Trim().Length > 4000) return Task.FromResult(new RequotationResult(RequotationStatus.InvalidRequest));
        return store.RequestAsync(id, changes.Trim(), new(user.Subject, user.Email, user.Name), budgets.Subject, token);
    }
}
