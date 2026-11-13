using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Application.Features.Management;

public sealed record QuotationSummary(long QuotationCount, decimal TotalQuotedValue);
public sealed record QuotationItemSummary(long ItemCount, long TotalQuantity);
public sealed record QuotationItemRank(Guid ProjectId, string ProjectName, string? SgCode,
    string ClientName, CommercialItemSection Section, int SourceRow, string ItemNumber,
    string Location, string System, string Glass, string AluminumFinish, int Quantity,
    decimal UnitPrice, decimal TotalPrice);
public sealed record QuotationProjectRank(Guid ProjectId, string ProjectName, string? SgCode,
    string ClientName, DateTime ConfirmedAt, decimal ProjectTotal);
public sealed record QuotationPortfolioData(QuotationSummary Summary, QuotationItemSummary ItemSummary,
    IReadOnlyList<QuotationItemRank> HighestValueItems, IReadOnlyList<QuotationItemRank> HighestUnitPriceItems,
    IReadOnlyList<QuotationProjectRank> HighestValueProjects, QuotationMix Mix);
public sealed record QuotationRanking<T>(long Total, int Returned, bool HasMore, IReadOnlyList<T> Items);
public sealed record ManagementQuotationPortfolioResponse(string CurrencyCode, long QuotationCount,
    long ItemCount, long TotalQuantity, decimal TotalQuotedValue,
    QuotationRanking<QuotationItemRank> HighestValueItems,
    QuotationRanking<QuotationItemRank> HighestUnitPriceItems,
    QuotationRanking<QuotationProjectRank> HighestValueProjects, QuotationMix Mix);

public sealed class GetManagementQuotationPortfolioUseCase(IManagementReadStore store)
{
    public const int TopLimit = 10;

    public async Task<ManagementQuotationPortfolioResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var data = await store.GetQuotationPortfolioAsync(cancellationToken);
        // An exact sum can exceed the public numeric(20,2) contract. Never clamp or wrap it.
        if (data.Summary.TotalQuotedValue > Money.Maximum)
            throw new InvalidOperationException("Quotation portfolio exceeds the monetary contract.");
        return new(Money.CurrencyCode, data.Summary.QuotationCount, data.ItemSummary.ItemCount,
            data.ItemSummary.TotalQuantity, data.Summary.TotalQuotedValue,
            Ranking(data.ItemSummary.ItemCount, data.HighestValueItems),
            Ranking(data.ItemSummary.ItemCount, data.HighestUnitPriceItems),
            Ranking(data.Summary.QuotationCount, data.HighestValueProjects), data.Mix);
    }

    private static QuotationRanking<T> Ranking<T>(long total, IReadOnlyList<T> items) =>
        new(total, items.Count, total > items.Count, items);
}
