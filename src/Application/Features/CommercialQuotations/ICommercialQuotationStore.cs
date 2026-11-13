using SnG.Control.Application.Access;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.CommercialQuotations;

public sealed record CommercialQuotationState(Project Project, IReadOnlyList<ProjectTask> Tasks, BudgetQuotationVersion? Version,
    CommercialCatalog? Catalog, IReadOnlyList<CommercialCatalogItem> Items, IReadOnlyList<CommercialCatalogItemExtraction> Extractions,
    CommercialQuotation? Quotation)
{
    public CommercialQuotationStatus Validate(string headSubject, bool confirming)
    {
        if (Quotation is not null || Tasks.Any(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == Version?.VersionNumber)) return CommercialQuotationStatus.AlreadyConfirmed;
        var reviews = Tasks.Where(t => t.Type == ProjectTaskType.ReviewQuotation && t.Status != ProjectTaskStatus.Completed).ToArray();
        if (reviews.Length != 1 || reviews[0].Area != OperationalArea.Commercial || reviews[0].Status != ProjectTaskStatus.InProgress
            || reviews[0].StartedAt is null || reviews[0].CompletedAt is not null
            || !string.Equals(reviews[0].AssignedToGoogleSubject, headSubject, StringComparison.Ordinal)
            || Version is null || Version.VersionNumber != reviews[0].QuotationRound || Catalog is null || Items.Count == 0
            || Items.Count != Extractions.Count || Items.Any(i => !Extractions.Any(e => e.CatalogItemId == i.Id)))
            return CommercialQuotationStatus.InvalidWorkflowState;
        if (Version.ConfirmedFileStorageKey is null) return CommercialQuotationStatus.DocumentUnavailable;
        if (!string.Equals(Project.SgCode, Version.DocumentSgCode, StringComparison.Ordinal)) return CommercialQuotationStatus.SgCodeMismatch;
        if (confirming && string.IsNullOrWhiteSpace(Project.CreatedByGoogleSubject)) return CommercialQuotationStatus.ProjectCreatorUnavailable;
        return CommercialQuotationStatus.Success;
    }
}
public sealed record CommercialQuotationFileState(Project Project, CommercialQuotation? Quotation);
public interface ICommercialQuotationStore
{
    Task<CommercialQuotationState?> LoadAsync(Guid projectId, CancellationToken cancellationToken);
    Task<CommercialQuotationConfirmationResult> ConfirmAsync(CommercialQuotationState expected, string headSubject,
        CommercialQuotation quotation, IReadOnlyList<CommercialQuotationItem> items, byte[] bytes,
        CommercialQuotationPreviewResponse preview, CancellationToken cancellationToken);
    Task<CommercialQuotationFileState?> GetFileAsync(Guid projectId, ProjectReadScope scope, CancellationToken cancellationToken);
}
