using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SnG.Control.Infrastructure.Quotations;

internal sealed class QuotationCellReader(WorkbookPart workbook)
{
    private readonly SharedStringItem[] strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ToArray() ?? [];

    public string Text(Cell? cell)
    {
        if (cell is null) return "";
        if (cell.CellFormula is not null && cell.CellValue is null) throw new UnsupportedQuotationException();
        var raw = cell.CellValue?.Text ?? "";
        if (cell.DataType?.Value == CellValues.Error) throw new UnsupportedQuotationException();
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(raw, out var index) || index < 0 || index >= strings.Length) throw new UnsupportedQuotationException();
            return strings[index].InnerText;
        }
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        return raw;
    }

    public string ItemNumber(Cell? cell)
    {
        var text = Text(cell).Trim();
        if (text.Length == 0 || cell is null) return "";
        if (cell.DataType is not null && cell.DataType.Value != CellValues.Number)
        {
            if (cell.DataType.Value is var type && (type == CellValues.Boolean || type == CellValues.Date))
                throw new UnsupportedQuotationException();
            return text;
        }
        var value = Decimal(cell);
        var format = NumberFormat(cell);
        if (format is "General" or "@")
        {
            // Wider/exponential General layouts cannot be reproduced confidently without Excel rendering.
            if (Math.Abs(value) >= 100_000_000_000m) throw new UnsupportedQuotationException();
            return value.ToString("G29", CultureInfo.InvariantCulture);
        }
        if (format.Length > 0 && format.All(character => character == '0') && decimal.Truncate(value) == value)
            return value.ToString(format, CultureInfo.InvariantCulture);
        throw new UnsupportedQuotationException();
    }

    public decimal PositiveDecimal(Cell? cell)
    {
        var value = Decimal(cell);
        if (value <= 0) throw new UnsupportedQuotationException();
        return value;
    }

    public decimal NonnegativeDecimal(Cell? cell)
    {
        var value = Decimal(cell);
        if (value < 0) throw new UnsupportedQuotationException();
        return value;
    }

    private decimal Decimal(Cell? cell)
    {
        if (cell is null || (cell.DataType is not null && cell.DataType.Value != CellValues.Number))
            throw new UnsupportedQuotationException();
        var text = Text(cell);
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new UnsupportedQuotationException();
        return value;
    }

    private string NumberFormat(Cell cell)
    {
        uint formatId = 0;
        if (cell.StyleIndex is not null)
        {
            var formats = workbook.WorkbookStylesPart?.Stylesheet?.CellFormats?.Elements<CellFormat>().ToArray();
            if (formats is null || cell.StyleIndex.Value >= formats.Length) throw new UnsupportedQuotationException();
            formatId = formats[cell.StyleIndex.Value].NumberFormatId?.Value ?? 0;
        }
        if (formatId == 0) return "General";
        if (formatId == 1) return "0";
        if (formatId == 49) return "@";
        return workbook.WorkbookStylesPart?.Stylesheet?.NumberingFormats?.Elements<NumberingFormat>()
            .SingleOrDefault(format => format.NumberFormatId?.Value == formatId)?.FormatCode?.Value
            ?? throw new UnsupportedQuotationException();
    }
}
