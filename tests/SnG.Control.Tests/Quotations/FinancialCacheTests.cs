using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public sealed class FinancialCacheTests
{
    public static byte[] Flag(byte[] bytes, string flag)
    {
        using var output = new MemoryStream(); output.Write(bytes); output.Position = 0;
        using (var book = SpreadsheetDocument.Open(output, true))
        {
            var wb = book.WorkbookPart!;
            var calc = wb.Workbook!.CalculationProperties ??= new CalculationProperties();
            switch (flag)
            {
                case "force": calc.ForceFullCalculation = true; break;
                case "full": calc.FullCalculationOnLoad = true; break;
                case "pending": calc.CalculationCompleted = false; break;
                case "manual": calc.CalculationMode = CalculateModeValues.Manual; break;
                case "complete": calc.CalculationMode = CalculateModeValues.Auto; calc.CalculationCompleted = true; calc.ForceFullCalculation = false; calc.FullCalculationOnLoad = false; break;
                case "cell": wb.WorksheetParts.First().Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "N15").CellFormula!.CalculateCell = true; break;
                case "sheet": wb.WorksheetParts.First().Worksheet!.Append(new SheetCalculationProperties { FullCalculationOnLoad = true }); break;
            }
        }
        return output.ToArray();
    }

    [Theory]
    [InlineData("force")] [InlineData("full")] [InlineData("pending")]
    [InlineData("manual")] [InlineData("cell")] [InlineData("sheet")]
    public async Task ExplicitUnresolvedCalculationBlocksFinancialImportAndConfirmation(string flag)
    {
        var bytes = Flag(CommercialQuotationScenario.Workbook().Formula("N15", "AL15", "100").Number("AL15", 100).Build(), flag);
        var before = bytes.ToArray();
        Assert.Equal(CommercialQuotationStatus.RecalculationRequired, (await CommercialQuotationScenario.Parser().ParseAsync(bytes, "test.xlsx", default)).Status);
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        var taskStates = scenario.State!.Tasks.Select(t => t.Status).ToArray();
        Assert.Equal(CommercialQuotationStatus.RecalculationRequired, (await scenario.Preview().ExecuteAsync(scenario.State.Project.Id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(CommercialQuotationStatus.RecalculationRequired, (await scenario.Confirm().ExecuteAsync(scenario.State.Project.Id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(0, scenario.ConfirmCalls); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
        Assert.Equal(taskStates, scenario.State.Tasks.Select(t => t.Status)); Assert.Null(scenario.State.Quotation);
        Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData("AL15")] [InlineData("$AL$15")] [InlineData("IFERROR(AL15,\"\")")]
    [InlineData("iferror( $AL$15 , \"\" )")]
    public async Task DirectNumericAliasDisagreementIsDetectedBeforeRounding(string formula)
    {
        var bytes = CommercialQuotationScenario.Workbook().Formula("N15", formula, "100.001").Number("AL15", 100.002m).Build();
        Assert.Equal(CommercialQuotationStatus.InconsistentFormulaCache, (await CommercialQuotationScenario.Parser().ParseAsync(bytes, "test.xlsx", default)).Status);
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        Assert.Equal(CommercialQuotationStatus.InconsistentFormulaCache, (await scenario.Confirm().ExecuteAsync(scenario.State!.Project.Id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(0, scenario.ConfirmCalls); Assert.Null(scenario.State.Quotation);
    }

    [Fact]
    public async Task SavedCacheIsTheSourceWithoutFormulaEvaluationOrQuantityMultiplication()
    {
        var bytes = Flag(CommercialQuotationScenario.Workbook().Number("M15", 3)
            .Formula("N15", "IFERROR(AL15,\"\")", "5796318.8020675844").Number("AL15", 5796318.8020675844m)
            .Formula("O15", "N15*M15", "2278430.0516260075").Formula("O30", "SUM(O15:O29)", "22540967.465117045").Build(), "complete");
        var parsed = await CommercialQuotationScenario.Parser().ParseAsync(bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, parsed.Status);
        Assert.Equal(5796318.80m, parsed.Quotation!.Items[0].UnitPrice);
        Assert.Equal(2278430.05m, parsed.Quotation.Items[0].TotalPrice);
        Assert.Equal(3, parsed.Quotation.Items[0].Values.Quantity);
        Assert.Equal(22540967.47m, parsed.Quotation.ProjectTotal);
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        var budgetBefore = System.Text.Json.JsonSerializer.Serialize(scenario.State!.Items);
        var result = await scenario.Confirm().ExecuteAsync(scenario.State.Project.Id, bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, result.Status);
        Assert.Equal(parsed.Quotation.ProjectTotal, scenario.State.Quotation!.ProjectTotal);
        Assert.Equal(parsed.Quotation.Items[0].UnitPrice, scenario.Items[0].UnitPrice);
        Assert.Equal(parsed.Quotation.Items[0].TotalPrice, scenario.Items[0].TotalPrice);
        Assert.Equal(budgetBefore, System.Text.Json.JsonSerializer.Serialize(scenario.State.Items));
    }

    [Fact]
    public async Task BudgetGeneratedWorkbookRequiresExcelSaveBeforeFinancialConfirmation()
    {
        var original = CommercialQuotationScenario.Workbook().Formula("N15", "AL15", "100").Number("AL15", 100).Build();
        var scenario = await CommercialHandoffScenario.CreateAsync(original);
        var generated = await CommercialHandoffScenario.Generator().GenerateAsync(original, "SG0012", scenario.State!.WorkbookItems(), default);
        Assert.NotNull(generated);
        Assert.Equal(CommercialQuotationStatus.RecalculationRequired, (await CommercialQuotationScenario.Parser().ParseAsync(generated, "confirmed.xlsx", default)).Status);
        // Simulates an explicitly completed save, without calculating or replacing any monetary value.
        var saved = Flag(generated, "complete");
        Assert.Equal(CommercialQuotationStatus.Success, (await CommercialQuotationScenario.Parser().ParseAsync(saved, "reviewed.xlsx", default)).Status);
    }

    [Fact]
    public async Task LiteralMoneyIsNotRecalculatedOrBlockedByUnrelatedCalculationFlags()
    {
        var bytes = Flag(CommercialQuotationScenario.Workbook().Build(), "force");
        Assert.Equal(CommercialQuotationStatus.Success, (await CommercialQuotationScenario.Parser().ParseAsync(bytes, "test.xlsx", default)).Status);
    }
}
