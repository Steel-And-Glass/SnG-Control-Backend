using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Persistence.Stores;

internal enum MixDimension { System, Glass, Finish, Combination }
internal enum MixOrder { Quantity, ItemCount, QuotedValue }
internal sealed class MixRow
{
    public string Value { get; init; } = "";
    public string Glass { get; init; } = "";
    public long ItemCount { get; init; }
    public long TotalQuantity { get; init; }
    public decimal TotalQuotedValue { get; init; }
}

public sealed partial class EfManagementReadStore
{
    internal IQueryable<MixRow> MixGroups(MixDimension dimension)
    {
        // CatalogItemValues already trims on write. Keep exact stored labels and case.
        var source = context.CurrentCommercialItems().Select(i => new
        {
            Value = dimension == MixDimension.Glass ? i.Glass : dimension == MixDimension.Finish ? i.AluminumFinish : i.System,
            Glass = dimension == MixDimension.Combination ? i.Glass : "",
            i.Quantity,
            i.TotalPrice
        });
        return source.Where(i => i.Value != null && i.Value.Trim() != "" &&
                (dimension != MixDimension.Combination || (i.Glass != null && i.Glass.Trim() != "")))
            .GroupBy(i => new { Value = EF.Functions.Collate(i.Value, "C"), Glass = EF.Functions.Collate(i.Glass, "C") })
            .Select(g => new MixRow
            {
                Value = g.Key.Value,
                Glass = g.Key.Glass,
                ItemCount = g.LongCount(),
                TotalQuantity = g.Sum(i => (long)i.Quantity),
                TotalQuotedValue = g.Sum(i => i.TotalPrice)
            });
    }

    internal IQueryable<MixRow> MixRanking(MixDimension dimension, MixOrder order)
    {
        var groups = MixGroups(dimension);
        var ranked = order switch
        {
            MixOrder.Quantity => groups.OrderByDescending(g => g.TotalQuantity).ThenByDescending(g => g.ItemCount).ThenByDescending(g => g.TotalQuotedValue),
            MixOrder.ItemCount => groups.OrderByDescending(g => g.ItemCount).ThenByDescending(g => g.TotalQuantity).ThenByDescending(g => g.TotalQuotedValue),
            _ => groups.OrderByDescending(g => g.TotalQuotedValue).ThenByDescending(g => g.TotalQuantity).ThenByDescending(g => g.ItemCount)
        };
        return ranked.ThenBy(g => EF.Functions.Collate(g.Value, "C")).ThenBy(g => EF.Functions.Collate(g.Glass, "C"))
            .Take(GetManagementQuotationPortfolioUseCase.TopLimit);
    }

    private static QuotationMixEntry SimpleMix(MixRow row) =>
        new(row.Value, row.ItemCount, row.TotalQuantity, row.TotalQuotedValue);

    private async Task<QuotationMixDimension<T>> ReadMixAsync<T>(MixDimension dimension, long totalItems,
        Func<MixRow, T> project, CancellationToken cancellationToken)
    {
        // Check every category for overflow, including categories absent from the top slices.
        var coverage = await MixGroups(dimension).GroupBy(_ => 1).Select(g => new
        {
            Categories = g.LongCount(),
            SpecifiedItems = g.Sum(row => row.ItemCount),
            LargestValue = g.Max(row => row.TotalQuotedValue)
        }).SingleOrDefaultAsync(cancellationToken);
        if (coverage?.LargestValue > Money.Maximum)
            throw new InvalidOperationException("Quotation mix exceeds the monetary contract.");
        var quantity = await MixRanking(dimension, MixOrder.Quantity).ToListAsync(cancellationToken);
        var count = await MixRanking(dimension, MixOrder.ItemCount).ToListAsync(cancellationToken);
        var value = await MixRanking(dimension, MixOrder.QuotedValue).ToListAsync(cancellationToken);
        QuotationRanking<T> Ranking(List<MixRow> rows) => new(coverage?.Categories ?? 0, rows.Count,
            (coverage?.Categories ?? 0) > rows.Count, rows.Select(project).ToArray());
        var specified = coverage?.SpecifiedItems ?? 0;
        return new(totalItems, specified, totalItems - specified, Ranking(quantity), Ranking(count), Ranking(value));
    }
}
