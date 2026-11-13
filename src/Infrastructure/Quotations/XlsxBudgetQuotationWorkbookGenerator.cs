using System.Globalization;
using System.IO.Compression;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Quotations;

public sealed class XlsxBudgetQuotationWorkbookGenerator(IBudgetQuotationParser parser) : IBudgetQuotationWorkbookGenerator
{
    public async Task<byte[]?> GenerateAsync(ReadOnlyMemory<byte> original, string documentSgCode,
        IReadOnlyList<ConfirmedWorkbookItem> items, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(original.ToArray(), writable: false);
        if (await parser.ParseAsync(input, "original.xlsx", cancellationToken) is not BudgetQuotationParseResult.Success parsed
            || parsed.Document.SgCode != documentSgCode || items.Count != parsed.Items.Count
            || items.Select(item => (item.Section, item.SourceRow)).Distinct().Count() != items.Count) return null;
        foreach (var item in items)
        {
            var source = parsed.Items.SingleOrDefault(value => value.SourceRow == item.SourceRow && value.Section == item.Section);
            if (source is null || item.Extracted != new CatalogItemValues(source.ItemNumber, source.Location, source.System,
                source.Glass, source.AluminumFinish, source.LockType, source.Observations, source.Width, source.Height,
                source.SquareMeters, source.Quantity)) return null;
        }
        try
        {
            using var buffer = new MemoryStream();
            await buffer.WriteAsync(original, cancellationToken);
            buffer.Position = 0;
            var targets = new Dictionary<string, (string Value, string? Formula, uint? Style)>();
            string sheetPath, workbookPath;
            using (var package = SpreadsheetDocument.Open(buffer, true, new OpenSettings { AutoSave = false }))
            {
                var workbook = package.WorkbookPart!;
                var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(value =>
                    string.Equals(value.Name?.Value?.Trim(), "COTIZACIÓN", StringComparison.OrdinalIgnoreCase));
                var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
                sheetPath = part.Uri.OriginalString.TrimStart('/');
                workbookPath = workbook.Uri.OriginalString.TrimStart('/');
                var rows = QuotationWorksheetLayout.ReadRows(part, cancellationToken);
                var reader = new QuotationCellReader(workbook);
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = item.Confirmed;
                    Write(item.SourceRow, "A", values.ItemNumber);
                    Write(item.SourceRow, "B", values.Location);
                    WriteNumber("J", values.Width);
                    WriteNumber("K", values.Height);
                    WriteNumber("L", values.SquareMeters);
                    WriteNumber("M", values.Quantity);
                    var labels = new Dictionary<string, string>
                    {
                        ["SISTEMA"] = values.System,
                        ["CRISTAL"] = values.Glass,
                        ["ACABADO ALUMINIO"] = values.AluminumFinish,
                        ["CERRADURA"] = values.LockType,
                        ["OBSERVACIONES"] = values.Observations
                    };
                    // The parser validated the complete layout. Stop at the next header/section, including empty blocks.
                    foreach (var (row, cells) in rows.Where(pair => pair.Key >= item.SourceRow))
                    {
                        if (row > item.SourceRow && (reader.Text(cells.GetValueOrDefault("A")).Trim().ToUpperInvariant() is "ITEM" or "ÍTEM"
                            || cells.Where(pair => pair.Key is not ("N" or "O") && pair.Value.CellFormula is null
                                && (pair.Value.DataType?.Value == CellValues.SharedString || pair.Value.DataType?.Value == CellValues.InlineString || pair.Value.DataType?.Value == CellValues.String))
                                .Any(pair => reader.Text(pair.Value).Trim().ToUpperInvariant() == "ADICIONALES"))) break;
                        var label = reader.Text(cells.GetValueOrDefault("C")).Trim().ToUpperInvariant();
                        if (labels.Remove(label, out var value)) Write(row, "D", value);
                    }
                    if (labels.Count != 0) return null;
                    void WriteNumber(string column, decimal value) => Write(item.SourceRow, column, value.ToString(CultureInfo.InvariantCulture), true);
                }
                part.Worksheet!.Save();
                var calculation = workbook.Workbook.CalculationProperties ??= new CalculationProperties();
                calculation.CalculationMode = CalculateModeValues.Auto;
                calculation.FullCalculationOnLoad = true;
                calculation.ForceFullCalculation = true;
                workbook.Workbook.Save();

                void Write(int rowIndex, string column, string value, bool number = false)
                {
                    var reference = column + rowIndex.ToString(CultureInfo.InvariantCulture);
                    var cells = rows[rowIndex];
                    if (!cells.TryGetValue(column, out var cell))
                    {
                        cell = new Cell { CellReference = reference };
                        var row = part.Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Single(value => value.RowIndex?.Value == (uint)rowIndex);
                        var next = row.Elements<Cell>().FirstOrDefault(value => CompareColumns(value.CellReference!.Value!, column) > 0);
                        row.InsertBefore(cell, next);
                        cells.Add(column, cell);
                    }
                    targets.Add(reference, (value, cell.CellFormula?.OuterXml, cell.StyleIndex?.Value));
                    cell.InlineString = null;
                    cell.CellValue = null;
                    if (number || cell.CellFormula is not null)
                    {
                        cell.DataType = number ? CellValues.Number : CellValues.String;
                        cell.CellValue = new CellValue(value);
                    }
                    else
                    {
                        cell.DataType = CellValues.InlineString;
                        cell.InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var output = buffer.ToArray();
            // Closing then reopening catches package/cell failures before the persistence phase.
            using (var reopened = SpreadsheetDocument.Open(new MemoryStream(output, writable: false), false))
            {
                var workbook = reopened.WorkbookPart!;
                var part = workbook.WorksheetParts.Single(value => value.Uri.OriginalString.TrimStart('/') == sheetPath);
                var cells = part.Worksheet!.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
                var reader = new QuotationCellReader(workbook);
                foreach (var (reference, expected) in targets)
                {
                    var cell = cells[reference];
                    if (reader.Text(cell) != expected.Value || cell.CellFormula?.OuterXml != expected.Formula
                        || cell.StyleIndex?.Value != expected.Style) return null;
                }
            }
            // Every other part, including images, anchors, relationships, styles and shared strings, must be byte-identical.
            using var before = new ZipArchive(new MemoryStream(original.ToArray()), ZipArchiveMode.Read);
            using var after = new ZipArchive(new MemoryStream(output), ZipArchiveMode.Read);
            if (!before.Entries.Select(entry => entry.FullName).Order().SequenceEqual(after.Entries.Select(entry => entry.FullName).Order())) return null;
            foreach (var entry in before.Entries.Where(entry => entry.FullName != sheetPath && entry.FullName != workbookPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var left = entry.Open();
                using var right = after.GetEntry(entry.FullName)!.Open();
                using var leftBytes = new MemoryStream();
                using var rightBytes = new MemoryStream();
                left.CopyTo(leftBytes); right.CopyTo(rightBytes);
                if (!leftBytes.ToArray().AsSpan().SequenceEqual(rightBytes.ToArray())) return null;
            }
            return output;
        }
        catch (Exception exception) when (exception is UnsupportedQuotationException or OpenXmlPackageException or InvalidDataException or XmlException
            or FormatException or OverflowException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static int CompareColumns(string reference, string column)
    {
        var existing = new string(reference.TakeWhile(char.IsLetter).ToArray());
        return existing.Length != column.Length ? existing.Length.CompareTo(column.Length) : string.CompareOrdinal(existing, column);
    }
}
