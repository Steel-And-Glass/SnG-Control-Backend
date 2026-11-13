using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Budgets.PreviewQuotation;

// Canonical decimal strings preserve COP cents across JSON/JavaScript boundaries.
public sealed record BudgetEconomicItem(CommercialItemSection Section, int SourceRow, string UnitPrice, string TotalPrice);
public sealed record BudgetQuotationEconomics(string Status, string? ProjectTotal, string? ProjectTotalCell,
    IReadOnlyList<BudgetEconomicItem> Items, bool? MainItemTotalsDiffer)
{
    public string CurrencyCode => "COP";
    public static BudgetQuotationEconomics Unavailable(string status) => new(status, null, null, [], null);
}
public sealed record BudgetEconomicRow(CommercialItemSection Section, int SourceRow);
public interface IBudgetQuotationEconomicsReader
{
    Task<BudgetQuotationEconomics> ReadAsync(Stream original, IReadOnlyList<BudgetEconomicRow> items, CancellationToken token);
}
