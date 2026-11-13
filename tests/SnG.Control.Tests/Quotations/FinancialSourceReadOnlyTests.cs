using System.Security.Cryptography;
using System.Globalization;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public sealed class FinancialSourceFactAttribute : FactAttribute
{
    public FinancialSourceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNG_FINANCIAL_SOURCE_XLSX")))
            Skip = "Optional read-only financial source outside Git.";
    }
}

public sealed class FinancialSourceReadOnlyTests
{
    [FinancialSourceFact]
    public async Task SavedMoneyMatchesParserWithoutChangingSourceBytes()
    {
        var path = Environment.GetEnvironmentVariable("SNG_FINANCIAL_SOURCE_XLSX")!;
        var original = await File.ReadAllBytesAsync(path);
        var parsed = await CommercialQuotationScenario.Parser().ParseAsync(original, "source.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, parsed.Status);
        var options = Options.Create(new BudgetQuotationParserOptions());
        var budget = Assert.IsType<BudgetQuotationParseResult.Success>(await new XlsxBudgetQuotationParser(options).ParseAsync(new MemoryStream(original, false), "source.xlsx", default));
        Assert.Equal("Success", budget.Economics!.Status);
        Assert.Equal(parsed.Quotation!.ProjectTotal.ToString("0.00", CultureInfo.InvariantCulture), budget.Economics.ProjectTotal);
        using var originalStream = new MemoryStream(original, false);
        var historical = await new XlsxBudgetQuotationEconomicsReader(options).ReadAsync(originalStream,
            budget.Items.Select(i => new BudgetEconomicRow(i.Section, i.SourceRow)).ToArray(), default);
        Assert.Equal(budget.Economics.ProjectTotal, historical.ProjectTotal);
        Assert.Equal(budget.Economics.Items, historical.Items);
        foreach (var item in parsed.Quotation.Items)
        {
            var amount = budget.Economics.Items.Single(i => i.Section == item.Key.Section && i.SourceRow == item.Key.SourceRow);
            Assert.Equal(item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture), amount.UnitPrice);
            Assert.Equal(item.TotalPrice.ToString("0.00", CultureInfo.InvariantCulture), amount.TotalPrice);
        }
        using var input = new MemoryStream(original, false);
        using var book = SpreadsheetDocument.Open(input, false);
        var wb = book.WorkbookPart!;
        var sheet = wb.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name!.Value!.Trim() == "COTIZACIÓN");
        var rows = QuotationWorksheetLayout.ReadRows((WorksheetPart)wb.GetPartById(sheet.Id!), default);
        var reader = new QuotationCellReader(wb);
        foreach (var item in parsed.Quotation!.Items)
        {
            Assert.Equal(Money.Normalize(reader.NonnegativeDecimal(rows[item.Key.SourceRow].GetValueOrDefault("N"))), item.UnitPrice);
            Assert.Equal(Money.Normalize(reader.NonnegativeDecimal(rows[item.Key.SourceRow].GetValueOrDefault("O"))), item.TotalPrice);
            Assert.Equal(reader.PositiveDecimal(rows[item.Key.SourceRow].GetValueOrDefault("M")), item.Values.Quantity);
        }
        var additional = rows.Single(r => reader.Text(r.Value.GetValueOrDefault("A")).Trim() == "ADICIONALES").Key;
        var total = rows.Single(r => r.Key < additional && reader.Text(r.Value.GetValueOrDefault("N")).Trim() == "VALOR TOTAL");
        Assert.Equal(Money.Normalize(reader.NonnegativeDecimal(total.Value.GetValueOrDefault("O"))), parsed.Quotation.ProjectTotal);
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }
}
