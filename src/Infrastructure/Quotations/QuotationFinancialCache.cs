using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SnG.Control.Application.Features.CommercialQuotations;

namespace SnG.Control.Infrastructure.Quotations;

// Read-only diagnostics, not a formula evaluator. No computed price replaces a saved value.
internal static partial class QuotationFinancialCache
{
    public static CommercialQuotationStatus Validate(WorkbookPart workbook,
        IReadOnlyDictionary<int, Dictionary<string, Cell>> rows, Cell?[] monetaryCells, QuotationCellReader reader)
    {
        var formulas = monetaryCells.Where(c => c?.CellFormula != null).Select(c => c!).ToArray();
        if (formulas.Length == 0) return CommercialQuotationStatus.Success;
        var calc = workbook.Workbook!.CalculationProperties;
        if (calc?.ForceFullCalculation?.Value == true || calc?.FullCalculationOnLoad?.Value == true ||
            calc?.CalculationCompleted?.Value == false || calc?.CalculationMode?.Value == CalculateModeValues.Manual ||
            workbook.WorksheetParts.Any(p => p.Worksheet?.GetFirstChild<SheetCalculationProperties>()?.FullCalculationOnLoad?.Value == true) ||
            formulas.Any(c => c.CellFormula!.CalculateCell?.Value == true))
            return CommercialQuotationStatus.RecalculationRequired;

        foreach (var cell in formulas)
        {
            // Only local numeric aliases, optionally IFERROR(ref,""), have a verifiable identity
            // without calculating business formulas. Shared/array formulas and all other syntax are untouched.
            if (cell.CellFormula!.FormulaType?.Value == CellFormulaValues.Shared || cell.CellFormula.FormulaType?.Value == CellFormulaValues.Array) continue;
            var formula = cell.CellFormula.Text.Trim().TrimStart('=');
            var match = Alias().Match(formula);
            if (!match.Success) continue;
            var reference = match.Groups["ref"].Value.Replace("$", "").ToUpperInvariant();
            var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
            if (!int.TryParse(reference[column.Length..], out var row) || !rows.TryGetValue(row, out var cells) ||
                !cells.TryGetValue(column, out var source)) continue;
            if (source.DataType != null && source.DataType.Value != CellValues.Number) continue;
            if (source.CellValue == null) continue; // No evidence of numerical disagreement.
            if (reader.NonnegativeDecimal(cell) != reader.NonnegativeDecimal(source))
                return CommercialQuotationStatus.InconsistentFormulaCache;
        }
        return CommercialQuotationStatus.Success;
    }

    [GeneratedRegex(@"^(?:IFERROR\(\s*(?<ref>\$?[A-Z]{1,3}\$?[1-9][0-9]*)\s*,\s*""""\s*\)|(?<ref>\$?[A-Z]{1,3}\$?[1-9][0-9]*))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Alias();
}
