using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Budgets.CommercialHandoff;

// Facts can come from a full command snapshot or a metadata-only read projection.
// One rule is used before generation, again under the send lock, and by GET status.
public sealed record CommercialHandoffReadiness(int? VersionNumber, bool HasOperationalFile, bool HasCatalog,
    int ItemCount, int ExtractionCount, int DistinctExtractionItems, bool AllItemsCovered, string? DocumentSgCode)
{
    public CommercialHandoffStatus Validate(Project project, IReadOnlyList<ProjectTask> tasks)
    {
        var round = BudgetQuotationWorkflow.CurrentRound(tasks, ProjectTaskType.PrepareQuotation);
        var currentTasks = tasks.Where(t => t.QuotationRound == round).ToArray();
        if (currentTasks.Any(task => task.Type == ProjectTaskType.ReviewQuotation) || (HasOperationalFile && VersionNumber == round)
            || currentTasks.Any(task => task.Type == ProjectTaskType.PrepareQuotation && task.Status == ProjectTaskStatus.Completed))
            return CommercialHandoffStatus.AlreadySent;
        if (BudgetQuotationWorkflow.ReadyTask(project, tasks) is null || VersionNumber != round
            || !HasCatalog || ItemCount == 0 || ExtractionCount != ItemCount
            || DistinctExtractionItems != ItemCount || !AllItemsCovered)
            return CommercialHandoffStatus.InvalidState;
        if (!string.Equals(project.SgCode, DocumentSgCode, StringComparison.Ordinal))
            return CommercialHandoffStatus.SgChangedAfterConfirmation;
        return CommercialHandoffStatus.Sent;
    }
}