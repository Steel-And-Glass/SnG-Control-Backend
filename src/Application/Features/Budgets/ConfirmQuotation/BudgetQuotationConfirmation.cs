using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.ProjectTasks;

namespace SnG.Control.Application.Features.Budgets.ConfirmQuotation;

public sealed record ConfirmBudgetQuotationRequest(IReadOnlyList<ConfirmedQuotationItem>? Items);
public sealed record ConfirmedQuotationItem(CommercialItemSection Section, int SourceRow, string ItemNumber,
    string? Location, string? System, string? Glass, string? AluminumFinish, string? LockType, string? Observations,
    decimal Width, decimal Height, decimal SquareMeters, int Quantity);

public sealed record QuotationFile(string StorageKey, ReadOnlyMemory<byte> Bytes);
public sealed record BudgetQuotationConfirmation(BudgetQuotationVersion Version, CommercialCatalog Catalog,
    IReadOnlyList<CommercialCatalogItem> Items, IReadOnlyList<CommercialCatalogItemExtraction> Extractions,
    IReadOnlyList<QuotationFile> Files);

public enum QuotationConfirmationStatus { Created, ProjectNotFound, InvalidWorkflowState, SgCodeMismatch, AlreadyConfirmed, InvalidWorkbook, InvalidConfirmation }
public sealed record QuotationConfirmationStoreResult(QuotationConfirmationStatus Status, ProjectTask? Task = null);
public sealed record ConfirmBudgetQuotationResult(QuotationConfirmationStatus Status,
    ConfirmedBudgetQuotationResponse? Response = null, BudgetQuotationFileError? FileError = null);

public sealed record ConfirmedBudgetQuotationResponse(Guid QuotationVersionId, int VersionNumber, Guid CommercialCatalogId,
    Guid ProjectId, DateTime ConfirmedAt, BudgetQuotationDocument Document, int ItemCount, long TotalQuantity,
    IReadOnlyList<ConfirmedCatalogItemResponse> Items, ProjectTaskResponse PrepareQuotationTask, BudgetQuotationEconomics? Economics = null);
public sealed record ConfirmedCatalogItemResponse(Guid Id, CommercialItemSection Section, int SourceRow,
    CatalogItemValues Extracted, CatalogItemValues Confirmed, CatalogItemImageResponse Image);
public sealed record CatalogItemImageResponse(CatalogItemImageStatus Status, string? ContentType);
