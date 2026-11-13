using SnG.Control.Application.Access;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.ClientQuotationDecisions;

public enum DecisionStatus { Success, Forbidden, ProjectNotFound, InvalidRequest, InvalidWorkflowState, QuotationUnavailable, AlreadyRecorded }
public sealed record DecisionResponse(bool Available, Guid ProjectId, Guid? CommercialQuotationId,
    Guid? PresentQuotationTaskId, ClientQuotationDecisionType? Decision, string? Note, DateTime? RecordedAt, int? QuotationRound = null)
{
    public static DecisionResponse From(Guid projectId, ClientQuotationDecision? value, int? round = null) => new(value is not null, projectId,
        value?.CommercialQuotationId, value?.PresentQuotationTaskId, value?.Decision, value?.Note, value?.RecordedAt, round);
}
public sealed record RecordedDecisionResponse(DecisionResponse Outcome, ProjectTaskResponse Task);
public sealed record DecisionResult(DecisionStatus Status, DecisionResponse? Response = null, RecordedDecisionResponse? Recorded = null);
public sealed record DecisionActor(string Subject, string? Email, string? Name);
public interface IClientQuotationDecisionStore
{
    Task<DecisionResult> ReadAsync(Guid projectId, ProjectReadScope scope, CancellationToken token);
    Task<DecisionResult> RecordAsync(Guid projectId, ClientQuotationDecisionType decision, string? note, DecisionActor actor, CancellationToken token);
}
public sealed class ClientQuotationDecisionService(IClientQuotationDecisionStore store, ICurrentUser user, IActiveRoleContext active)
{
    public Task<DecisionResult> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, new(user.Subject, active.Role), token);
    public Task<DecisionResult> RecordAsync(Guid id, ClientQuotationDecisionType decision, string? note, CancellationToken token)
    {
        if (active.Role != SystemRole.CommercialAdvisor) return Task.FromResult(new DecisionResult(DecisionStatus.Forbidden));
        if (!Enum.IsDefined(decision) || note?.Length > 2000) return Task.FromResult(new DecisionResult(DecisionStatus.InvalidRequest));
        return store.RecordAsync(id, decision, note, new(user.Subject, user.Email, user.Name), token);
    }
}

public static class DecisionWorkflow
{
    public static DecisionStatus Validate(Guid projectId, CommercialQuotation? quotation, IReadOnlyList<ProjectTask> tasks, string subject, int round = 1)
    {
        if (quotation is null) return DecisionStatus.QuotationUnavailable;
        var presentations = tasks.Where(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == round).ToArray();
        if (quotation.ProjectId != projectId || presentations.Length != 1) return DecisionStatus.InvalidWorkflowState;
        var reviews = tasks.Where(t => t.Type == ProjectTaskType.ReviewQuotation && t.QuotationRound == round).ToArray();
        if (reviews.Length != 1 || reviews[0].ProjectId != projectId || reviews[0].Area != OperationalArea.Commercial
            || reviews[0].Status != ProjectTaskStatus.Completed || reviews[0].CompletedAt is null)
            return DecisionStatus.InvalidWorkflowState;
        var task = presentations[0];
        if (task.ProjectId != projectId || task.Area != OperationalArea.Commercial) return DecisionStatus.InvalidWorkflowState;
        if (!string.Equals(task.AssignedToGoogleSubject, subject, StringComparison.Ordinal)) return DecisionStatus.Forbidden;
        if (task.Status != ProjectTaskStatus.InProgress || task.StartedAt is null || task.CompletedAt is not null)
            return DecisionStatus.InvalidWorkflowState;
        return DecisionStatus.Success;
    }
}
