using System.Globalization;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public class CommercialEconomicsTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(147)]
    [InlineData(169)]
    [InlineData(217)]
    [InlineData(245)]
    [InlineData(302)]
    [InlineData(343)]
    [InlineData(344)]
    [InlineData(364)]
    public async Task DynamicTotal_IgnoresAdditionalTotalAndDoesNotCalculate(int row)
    {
        var bytes = CommercialQuotationScenario.Workbook(row).Formula("N15", "1/0", "7.125")
            .Formula("O15", "N15*M15", "99.9876").Formula($"O{row}", "1/0", "123.4567").Build();
        var result = await CommercialQuotationScenario.Parser().ParseAsync(bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, result.Status);
        Assert.Equal(123.46m, result.Quotation!.ProjectTotal);
        Assert.Equal(7.13m, result.Quotation.Items[0].UnitPrice); Assert.Equal(99.99m, result.Quotation.Items[0].TotalPrice);
        Assert.NotEqual(result.Quotation.Items.Sum(i => i.TotalPrice), result.Quotation.ProjectTotal);
        Assert.Equal(2, result.Quotation.Items.Count);
    }
    [Theory]
    [InlineData("N15")]
    [InlineData("O15")]
    [InlineData("O30")]
    public async Task Values_NormalizeZeroAndLargeFractionsInAnyCulture(string cell)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-CO");
            foreach (var value in new[] { 0m, 123456789012345678.12345678m })
            {
                var parsed = await CommercialQuotationScenario.Parser().ParseAsync(CommercialQuotationScenario.Workbook().Number(cell, value).Build(), "test.xlsx", default);
                Assert.Equal(CommercialQuotationStatus.Success, parsed.Status);
                Assert.Equal(Money.Normalize(value), cell == "N15" ? parsed.Quotation!.Items[0].UnitPrice : cell == "O15" ? parsed.Quotation!.Items[0].TotalPrice : parsed.Quotation!.ProjectTotal);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData("N15", "cache")]
    [InlineData("O15", "cache")]
    [InlineData("O30", "cache")]
    [InlineData("N15", "negative")]
    [InlineData("O15", "negative")]
    [InlineData("O30", "negative")]
    [InlineData("N15", "text")]
    [InlineData("O15", "text")]
    [InlineData("O30", "text")]
    [InlineData("N15", "invalid")]
    [InlineData("O15", "invalid")]
    [InlineData("O30", "invalid")]
    public async Task InvalidEconomics_IsRejected(string cell, string mode)
    {
        var book = CommercialQuotationScenario.Workbook();
        if (mode == "cache") book.Formula(cell, "1+1", null);
        if (mode == "negative") book.Number(cell, -1);
        if (mode == "text") book.Text(cell, "123,45");
        if (mode == "invalid") book.Formula(cell, "1+1", "not-a-number");
        Assert.Equal(CommercialQuotationStatus.InvalidEconomics, (await CommercialQuotationScenario.Parser().ParseAsync(book.Build(), "test.xlsx", default)).Status);
    }
    [Theory]
    [InlineData("missing-total")]
    [InlineData("ambiguous-total")]
    [InlineData("missing-additional")]
    public async Task EconomicStructure_DoesNotGuess(string mode)
    {
        var book = CommercialQuotationScenario.Workbook();
        if (mode == "missing-total") book.Text("N30", "OTHER");
        if (mode == "ambiguous-total") book.Text("N29", "VALOR TOTAL").Number("O29", 1);
        if (mode == "missing-additional") book.Text("A39", "OTHER");
        var result = await CommercialQuotationScenario.Parser().ParseAsync(book.Build(), "test.xlsx", default);
        Assert.Equal(mode == "missing-additional" ? CommercialQuotationStatus.InvalidWorkbook : CommercialQuotationStatus.InvalidEconomics, result.Status);
        if (mode == "missing-additional") Assert.Equal(SnG.Control.Application.Features.Budgets.PreviewQuotation.BudgetQuotationFileError.UnsupportedWorkbookFormat, result.FileError);
    }
}
