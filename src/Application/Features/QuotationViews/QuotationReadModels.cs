using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Application.Features.QuotationViews;

public sealed record BudgetQuotationReadItem(Guid Id, CommercialItemSection Section, int SourceRow,
    CatalogItemValues Confirmed, CatalogItemValues? Extracted, CatalogItemImageStatus? ImageStatus,
    bool HasImage, IReadOnlyList<TechnicalDifference> TechnicalDifferences);
public sealed record ConfirmedBudgetQuotationView(Guid ProjectId, Guid QuotationVersionId, int VersionNumber,
    DateTime ConfirmedAt, BudgetQuotationDocument Document, IReadOnlyList<BudgetQuotationReadItem> Items, BudgetQuotationEconomics? Economics = null);
public sealed record ConfirmedCommercialQuotationView(Guid ProjectId, Guid CommercialQuotationId, DateTime ConfirmedAt,
    decimal ProjectTotal, IReadOnlyList<CommercialQuotationReadItem> Items, int VersionNumber = 1)
{
    public string CurrencyCode => Money.CurrencyCode;
}
public sealed record CommercialQuotationReadItem(CommercialItemSection Section, int SourceRow, Guid CommercialCatalogItemId,
    CatalogItemValues Values, decimal UnitPrice, decimal TotalPrice, IReadOnlyList<TechnicalDifference> TechnicalDifferences,
    CatalogItemImageStatus? ImageStatus, bool HasImage);

public sealed record QuotationReadAccess(Project Project, IReadOnlyList<ProjectTask> Tasks);
// Storage keys stay inside Application/Infrastructure; never part of the public JSON views.
public sealed record QuotationImageFile(string StorageKey, string ContentType);
public sealed record QuotationImageContent(Stream Content, string ContentType);
public enum QuotationReadStatus { Success, NotFound, Forbidden }
public sealed record QuotationReadResult<T>(QuotationReadStatus Status, T? Value = default);
public interface IQuotationReadStore
{
    Task<QuotationReadAccess?> GetAccessAsync(Guid projectId, CancellationToken token);
    Task<ConfirmedBudgetQuotationView?> GetBudgetAsync(Guid projectId, CancellationToken token);
    Task<string?> GetBudgetOriginalKeyAsync(Guid projectId, Guid versionId, CancellationToken token) => Task.FromResult<string?>(null);
    Task<ConfirmedCommercialQuotationView?> GetCommercialAsync(Guid projectId, CancellationToken token);
    Task<QuotationImageFile?> GetBudgetImageAsync(Guid projectId, Guid itemId, CancellationToken token);
}
