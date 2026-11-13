using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Budgets.PreviewQuotation;


public enum BudgetQuotationWarningCode { MissingImage = 1, AmbiguousImage = 2 }
public enum BudgetQuotationFileError { UnreadableWorkbook = 1, UnsupportedWorkbookFormat = 2, InvalidDocumentSgCode = 3, TechnicalLimitExceeded = 4 }

public sealed record BudgetQuotationDocument(string FileName, string ClientName, string ProjectName,
    string Location, string ClientLine, string ProductionLine, string PreparedBy, string SgCode);
public sealed record BudgetQuotationImage(string ContentType, string Base64);
public sealed record BudgetQuotationWarning(BudgetQuotationWarningCode Code, string ItemNumber, int SourceRow, string Message);
public sealed record BudgetQuotationPreviewItem(string ItemNumber, CommercialItemSection Section, string Location,
    string System, string Glass, string AluminumFinish, string LockType, string Observations,
    decimal Width, decimal Height, decimal SquareMeters, int Quantity, BudgetQuotationImage? Image, int SourceRow);
public sealed record BudgetQuotationPreviewResponse(Guid ProjectId, string ProjectSgCode,
    BudgetQuotationDocument Document, int ItemCount, long TotalQuantity,
    IReadOnlyList<BudgetQuotationPreviewItem> Items, IReadOnlyList<BudgetQuotationWarning> Warnings, int QuotationRound = 1, BudgetQuotationEconomics? Economics = null);
