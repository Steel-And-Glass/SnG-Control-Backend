using System.Globalization;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Application.Features.CommercialQuotations;

public enum CommercialQuotationStatus
{
    Success, ProjectNotFound, Forbidden, InvalidWorkflowState, AlreadyConfirmed, ProjectCreatorUnavailable,
    DocumentUnavailable, SgCodeMismatch, StructuralMismatch, InvalidWorkbook, InvalidEconomics, RecalculationRequired, InconsistentFormulaCache
}
public enum TechnicalField { ItemNumber, Location, System, Glass, AluminumFinish, LockType, Observations, Width, Height, SquareMeters, Quantity }
public sealed record CommercialItemKey(CommercialItemSection Section, int SourceRow);
public sealed record TechnicalDifference(TechnicalField Field, string PreviousValue, string CurrentValue);
public sealed record CommercialParsedItem(CommercialItemKey Key, CatalogItemValues Values, decimal UnitPrice, decimal TotalPrice);
public sealed record CommercialParsedQuotation(string FileName, string SgCode, decimal ProjectTotal, IReadOnlyList<CommercialParsedItem> Items);
public sealed record CommercialParseResult(CommercialQuotationStatus Status, CommercialParsedQuotation? Quotation = null, BudgetQuotationFileError? FileError = null);
public interface ICommercialQuotationParser
{
    Task<CommercialParseResult> ParseAsync(byte[] bytes, string fileName, CancellationToken cancellationToken);
}
public sealed record CommercialQuotationPreviewItem(CommercialItemSection Section, int SourceRow, Guid CommercialCatalogItemId,
    CatalogItemValues Values, decimal UnitPrice, decimal TotalPrice, IReadOnlyList<TechnicalDifference> TechnicalDifferences);
public sealed record CommercialQuotationPreviewResponse(Guid ProjectId, decimal ProjectTotal, IReadOnlyList<CommercialQuotationPreviewItem> Items, int QuotationRound = 1)
{
    public string CurrencyCode => Money.CurrencyCode;
    public int TechnicalDifferencesCount => Items.Sum(item => item.TechnicalDifferences.Count);
    public bool HasTechnicalDifferences => TechnicalDifferencesCount > 0;
}
public sealed record CommercialQuotationPreviewResult(CommercialQuotationStatus Status, CommercialQuotationPreviewResponse? Response = null,
    IReadOnlyList<CommercialItemKey>? MissingItems = null, IReadOnlyList<CommercialItemKey>? UnexpectedItems = null, BudgetQuotationFileError? FileError = null);
public sealed record CommercialQuotationConfirmationResponse(Guid ProjectId, Guid CommercialQuotationId, decimal ProjectTotal,
    bool CommercialFileAvailable, ProjectTaskResponse ReviewTask, ProjectTaskResponse PresentQuotationToClientTask,
    bool HasTechnicalDifferences, int TechnicalDifferencesCount)
{
    public string CurrencyCode => Money.CurrencyCode;
}
public sealed record CommercialQuotationConfirmationResult(CommercialQuotationStatus Status, CommercialQuotationConfirmationResponse? Response = null,
    CommercialQuotationPreviewResult? Validation = null);
public sealed record CommercialQuotationDownloadResult(CommercialQuotationStatus Status, byte[]? Bytes = null, string? FileName = null);

public static class CommercialTechnicalDifferences
{
    public static CatalogItemValues Values(CommercialCatalogItem item) => new(item.ItemNumber, item.Location, item.System, item.Glass,
        item.AluminumFinish, item.LockType, item.Observations, item.Width, item.Height, item.SquareMeters, item.Quantity);

    public static IReadOnlyList<TechnicalDifference> Compare(CatalogItemValues previous, CatalogItemValues current)
    {
        var result = new List<TechnicalDifference>();
        void Text(TechnicalField field, string before, string after) { if (!string.Equals(before, after, StringComparison.Ordinal)) result.Add(new(field, before, after)); }
        void Number(TechnicalField field, decimal before, decimal after)
        {
            decimal Normalize(decimal value) => field switch
            {
                TechnicalField.Width or TechnicalField.Height => TechnicalMeasurePrecision.Dimension(value),
                TechnicalField.SquareMeters => TechnicalMeasurePrecision.Surface(value),
                _ => value
            };
            var normalizedBefore = Normalize(before); var normalizedAfter = Normalize(after);
            if (normalizedBefore != normalizedAfter) result.Add(new(field,
                normalizedBefore.ToString("G29", CultureInfo.InvariantCulture), normalizedAfter.ToString("G29", CultureInfo.InvariantCulture)));
        }
        Text(TechnicalField.ItemNumber, previous.ItemNumber, current.ItemNumber); Text(TechnicalField.Location, previous.Location, current.Location);
        Text(TechnicalField.System, previous.System, current.System); Text(TechnicalField.Glass, previous.Glass, current.Glass);
        Text(TechnicalField.AluminumFinish, previous.AluminumFinish, current.AluminumFinish); Text(TechnicalField.LockType, previous.LockType, current.LockType);
        Text(TechnicalField.Observations, previous.Observations, current.Observations); Number(TechnicalField.Width, previous.Width, current.Width);
        Number(TechnicalField.Height, previous.Height, current.Height); Number(TechnicalField.SquareMeters, previous.SquareMeters, current.SquareMeters);
        Number(TechnicalField.Quantity, previous.Quantity, current.Quantity);
        return result;
    }
}
