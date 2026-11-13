using SnG.Control.Domain.Enums;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Application.Features.Budgets.Status;

public enum BudgetQuotationStatus { NotConfirmed = 1, Confirmed = 2, SentToCommercial = 3 }
public enum BudgetQuotationSendBlockReason { None = 0, SgChangedAfterConfirmation = 1, InvalidState = 2 }

public sealed record BudgetQuotationStatusResponse(Guid ProjectId, BudgetQuotationStatus Status,
    int? VersionNumber, DateTime? ConfirmedAt, int ItemCount, long TotalQuantity,
    string? DocumentSgCode, string? CurrentProjectSgCode, bool CanSendToCommercial,
    BudgetQuotationSendBlockReason SendBlockReason, int? QuotationRound = 1)
{
    public static BudgetQuotationStatusResponse From(Project project, IReadOnlyList<ProjectTask> tasks,
        CommercialHandoffReadiness facts, DateTime? confirmedAt, long totalQuantity)
    {
        var round = BudgetQuotationWorkflow.CurrentRound(tasks, ProjectTaskType.PrepareQuotation);
        if (facts.VersionNumber is null)
            return new(project.Id, BudgetQuotationStatus.NotConfirmed, null, null, 0, 0, null,
                project.SgCode, false, BudgetQuotationSendBlockReason.None, round);
        if (round > facts.VersionNumber)
            return new(project.Id, BudgetQuotationStatus.NotConfirmed, facts.VersionNumber, confirmedAt, facts.ItemCount,
                totalQuantity, facts.DocumentSgCode, project.SgCode, false, BudgetQuotationSendBlockReason.None, round);
        var readiness = facts.Validate(project, tasks);
        var sent = readiness == CommercialHandoffStatus.AlreadySent;
        var reason = readiness switch
        {
            CommercialHandoffStatus.SgChangedAfterConfirmation => BudgetQuotationSendBlockReason.SgChangedAfterConfirmation,
            CommercialHandoffStatus.InvalidState => BudgetQuotationSendBlockReason.InvalidState,
            _ => BudgetQuotationSendBlockReason.None
        };
        return new(project.Id, sent ? BudgetQuotationStatus.SentToCommercial : BudgetQuotationStatus.Confirmed,
            facts.VersionNumber, confirmedAt, facts.ItemCount, totalQuantity, facts.DocumentSgCode,
            project.SgCode, readiness == CommercialHandoffStatus.Sent, reason, round);
    }
}
public interface IBudgetQuotationStatusStore
{
    Task<BudgetQuotationStatusResponse?> GetAsync(Guid projectId, ProjectReadScope scope, CancellationToken cancellationToken);
}