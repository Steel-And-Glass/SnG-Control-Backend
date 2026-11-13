namespace SnG.Control.Application.Features.Management;

public sealed record QuotationMixEntry(string Value, long ItemCount, long TotalQuantity, decimal TotalQuotedValue);
public sealed record QuotationCombinationEntry(string System, string Glass, long ItemCount, long TotalQuantity, decimal TotalQuotedValue);
public sealed record QuotationMixDimension<T>(long TotalItems, long SpecifiedItems, long UnspecifiedItems,
    QuotationRanking<T> ByQuantity, QuotationRanking<T> ByItemCount, QuotationRanking<T> ByQuotedValue);
public sealed record QuotationMix(QuotationMixDimension<QuotationMixEntry> Systems,
    QuotationMixDimension<QuotationMixEntry> Glass, QuotationMixDimension<QuotationMixEntry> AluminumFinishes,
    QuotationMixDimension<QuotationCombinationEntry> SystemGlassCombinations);
