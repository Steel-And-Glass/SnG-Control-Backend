using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SnG.Control.Infrastructure.Quotations;

internal static class QuotationWorksheetLayout
{
    internal static SortedDictionary<int, Dictionary<string, Cell>> ReadRows(WorksheetPart worksheet, CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<int, Dictionary<string, Cell>>();
        var previous = 0;
        var sheetData = worksheet.Worksheet?.GetFirstChild<SheetData>() ?? throw new UnsupportedQuotationException();
        foreach (var row in sheetData.Elements<Row>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowIndex = checked((int)(row.RowIndex?.Value ?? 0));
            if (rowIndex <= previous || rowIndex > 1_048_576) throw new UnsupportedQuotationException();
            previous = rowIndex;
            var cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
            foreach (var cell in row.Elements<Cell>())
            {
                var reference = cell.CellReference?.Value ?? throw new UnsupportedQuotationException();
                var split = 0;
                while (split < reference.Length && reference[split] is >= 'A' and <= 'Z') split++;
                if (split == 0 || !int.TryParse(reference.AsSpan(split), NumberStyles.None, CultureInfo.InvariantCulture, out var cellRow)
                    || cellRow != rowIndex || !cells.TryAdd(reference[..split], cell)) throw new UnsupportedQuotationException();
            }
            result.Add(rowIndex, cells);
        }
        return result;
    }

}
