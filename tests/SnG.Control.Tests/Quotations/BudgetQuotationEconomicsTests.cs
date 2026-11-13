using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.Fixtures;
namespace SnG.Control.Tests.Quotations;

public sealed class BudgetQuotationEconomicsTests
{
    private static readonly IOptions<BudgetQuotationParserOptions> Options = Microsoft.Extensions.Options.Options.Create(new BudgetQuotationParserOptions());
    [Theory]
    [InlineData(1, 30)] [InlineData(3, 42)]
    public async Task PreviewAndHistoricalReadUseSavedCellsNotQuantityOrCommercialPrices(int quantity, int totalRow)
    {
        var bytes = CommercialQuotationScenario.Workbook(totalRow).Number("M15", quantity)
            .Formula("N15", "AL15", "5796318.8020675844").Number("AL15", 5796318.8020675844m)
            .Formula("O15", "N15*M15", "2278430.0516260075")
            .Formula($"O{totalRow}", "SUM(O15:O29)", "22540967.465117045").Build();
        var before = bytes.ToArray();
        var preview = Assert.IsType<BudgetQuotationParseResult.Success>(await new XlsxBudgetQuotationParser(Options).ParseAsync(new MemoryStream(bytes, false), "source.xlsx", default));
        var economics = Assert.IsType<BudgetQuotationEconomics>(preview.Economics);
        Assert.Equal("Success", economics.Status); Assert.Equal("COP", economics.CurrencyCode);
        Assert.Equal("22540967.47", economics.ProjectTotal); Assert.Equal($"O{totalRow}", economics.ProjectTotalCell);
        Assert.True(economics.MainItemTotalsDiffer);
        var item = economics.Items.Single(i => i.Section == CommercialItemSection.Main);
        Assert.Equal(CommercialItemSection.Main, item.Section); Assert.Equal(15, item.SourceRow);
        Assert.Equal("5796318.80", item.UnitPrice); Assert.Equal("2278430.05", item.TotalPrice);
        Assert.Equal(quantity, preview.Items[0].Quantity);
        using var stream = new MemoryStream(bytes, false);
        var saved = await new XlsxBudgetQuotationEconomicsReader(Options).ReadAsync(stream, economics.Items.Select(i => new BudgetEconomicRow(i.Section, i.SourceRow)).ToArray(), default);
        Assert.Equal(economics.ProjectTotal, saved.ProjectTotal); Assert.Equal(economics.Items, saved.Items);
        Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData("missing", "InvalidEconomics")]
    [InlineData("force", "RecalculationRequired")]
    [InlineData("mismatch", "InconsistentFormulaCache")]
    [InlineData("negative", "InvalidEconomics")]
    public async Task UnreliableAmountsAreExplicitWithoutChangingTechnicalPreview(string kind, string status)
    {
        var workbook = CommercialQuotationScenario.Workbook();
        if (kind == "force") workbook.Formula("N15", "AL15", "100").Number("AL15", 100);
        if (kind == "missing") workbook.Formula("N15", "AL15", null);
        if (kind == "mismatch") workbook.Formula("N15", "AL15", "100.001").Number("AL15", 100.002m);
        if (kind == "negative") workbook.Number("N15", -1);
        var bytes = workbook.Build();
        if (kind == "force") bytes = FinancialCacheTests.Flag(bytes, "force");
        var preview = Assert.IsType<BudgetQuotationParseResult.Success>(await new XlsxBudgetQuotationParser(Options).ParseAsync(new MemoryStream(bytes, false), "test.xlsx", default));
        Assert.Equal(status, preview.Economics!.Status); Assert.Null(preview.Economics.ProjectTotal); Assert.Empty(preview.Economics.Items);
        using var stream = new MemoryStream(bytes, false);
        var saved = await new XlsxBudgetQuotationEconomicsReader(Options).ReadAsync(stream, [new(CommercialItemSection.Main, 15)], default);
        Assert.Equal(status, saved.Status);
    }

    [Fact]
    public async Task AdditionalItemsHaveTheirOwnPricesButDoNotReplaceMainDocumentTotal()
    {
        var bytes = CommercialQuotationScenario.Workbook().Number("N41", 10.005m).Number("O41", 20.005m).Number("O30", 246912m).Build();
        var parsed = Assert.IsType<BudgetQuotationParseResult.Success>(await new XlsxBudgetQuotationParser(Options).ParseAsync(new MemoryStream(bytes, false), "test.xlsx", default));
        Assert.Equal("Success", parsed.Economics!.Status);
        Assert.Equal("246912.00", parsed.Economics.ProjectTotal);
        Assert.False(parsed.Economics.MainItemTotalsDiffer);
        Assert.Equal("10.01", parsed.Economics.Items[1].UnitPrice);
        Assert.Equal("20.01", parsed.Economics.Items[1].TotalPrice);
    }
}
