using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace SnG.Control.Tests.Fixtures;

public sealed class QuotationWorkbook
{
    public static readonly byte[] FirstImage = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    public static readonly byte[] SecondImage = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private readonly SortedDictionary<int, Dictionary<string, Cell>> rows = [];
    private readonly List<(int Row, byte[] Bytes, bool TwoCell, string ContentType)> pictures = [];
    public string SheetName { get; set; } = " COTIZACIÓN ";
    public SpreadsheetDocumentType DocumentType { get; set; } = SpreadsheetDocumentType.Workbook;

    public QuotationWorkbook(string sgCode = "SG0012")
    {
        Text("C10", "Cliente sintético"); Text("C11", "Proyecto sintético"); Text("C12", "Ubicación sintética");
        Text("I10", "Línea cliente"); Text("I11", "Línea producción"); Text("I12", "Equipo de pruebas"); Text("O10", sgCode);
    }

    public QuotationWorkbook Item(int headerRow = 14, string number = "01", bool image = true, bool additional = false)
    {
        if (additional) Text($"A{headerRow - 1}", " ADICIONALES ");
        var headers = new Dictionary<string, string>
        {
            ["A"] = "ÍTEM",
            ["B"] = "UBICACIÓN",
            ["C"] = "CONFIGURACIÓN",
            ["D"] = "DESCRIPCIÓN",
            ["J"] = "AN (MT)",
            ["K"] = "AL (MT)",
            ["L"] = "M2",
            ["M"] = "CNT",
            ["N"] = "VR UNT",
            ["O"] = "VR TOTAL"
        };
        foreach (var (column, label) in headers) Text($"{column}{headerRow}", label);
        var row = headerRow + 1;
        Text($"A{row}", number); Text($"B{row}", "Zona sintética");
        Number($"J{row}", 1m); Number($"K{row}", 2.8m); Number($"L{row}", 5.6m); Number($"M{row}", 2m);
        Number($"N{row}", 123456m); Number($"O{row}", 246912m);
        // Deliberately not the canonical order: the parser must use labels, not offsets.
        var config = new[] { ("CRISTAL", "Vidrio prueba"), ("SISTEMA", "Sistema prueba"), ("OBSERVACIONES", ""),
            ("CERRADURA", "Cierre prueba"), ("PELÍCULA PROTECCIÓN", "NO EXPORTAR"), ("ACABADO ALUMINIO", "Acabado prueba") };
        for (var index = 0; index < config.Length; index++)
        {
            Text($"C{row + index}", config[index].Item1); Text($"D{row + index}", config[index].Item2);
        }
        if (image) Image(row, FirstImage);
        return this;
    }

    public QuotationWorkbook Text(string reference, string value)
    {
        Set(reference, new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) });
        return this;
    }

    public QuotationWorkbook Number(string reference, decimal value, uint? style = null)
    {
        Set(reference, new Cell { CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)), StyleIndex = style });
        return this;
    }

    public QuotationWorkbook Formula(string reference, string formula, string? cache)
    {
        Set(reference, new Cell { CellFormula = new CellFormula(formula), CellValue = cache is null ? null : new CellValue(cache) });
        return this;
    }

    public QuotationWorkbook Image(int row, byte[]? bytes = null, bool twoCell = false, string contentType = "image/png")
    {
        pictures.Add((row, bytes ?? FirstImage, twoCell, contentType));
        return this;
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, DocumentType))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "00" },
                new NumberingFormat { NumberFormatId = 165, FormatCode = "0.00%" })
            { Count = 2 },
                new CellFormats(new CellFormat { NumberFormatId = 0 }, new CellFormat { NumberFormatId = 164 }, new CellFormat { NumberFormatId = 165 }) { Count = 3 });
            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            foreach (var (row, cells) in rows)
                data.Append(new Row(cells.OrderBy(cell => ColumnNumber(cell.Key)).Select(cell => cell.Value.CloneNode(true))) { RowIndex = (uint)row });
            worksheet.Worksheet = new Worksheet(data);
            workbook.Workbook.Append(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = SheetName }));
            if (pictures.Count > 0)
            {
                var drawings = worksheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing();
                worksheet.Worksheet.Append(new Drawing { Id = worksheet.GetIdOfPart(drawings) });
                uint id = 1;
                // Reverse package order to prove row anchors, not ZIP order, determine association.
                foreach (var (row, bytes, twoCell, contentType) in pictures.AsEnumerable().Reverse())
                {
                    var image = drawings.AddImagePart(contentType);
                    using (var input = new MemoryStream(bytes)) image.FeedData(input);
                    var from = new Xdr.FromMarker(new Xdr.ColumnId("3"), new Xdr.ColumnOffset("0"), new Xdr.RowId((row - 1).ToString(CultureInfo.InvariantCulture)), new Xdr.RowOffset("0"));
                    var picture = new Xdr.Picture(new Xdr.NonVisualPictureProperties(new Xdr.NonVisualDrawingProperties { Id = id++, Name = "Synthetic" }, new Xdr.NonVisualPictureDrawingProperties()),
                        new Xdr.BlipFill(new A.Blip { Embed = drawings.GetIdOfPart(image) }, new A.Stretch(new A.FillRectangle())),
                        new Xdr.ShapeProperties(new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
                    if (twoCell)
                        drawings.WorksheetDrawing.Append(new Xdr.TwoCellAnchor(from,
                            new Xdr.ToMarker(new Xdr.ColumnId("6"), new Xdr.ColumnOffset("0"), new Xdr.RowId(row.ToString(CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")), picture, new Xdr.ClientData()));
                    else drawings.WorksheetDrawing.Append(new Xdr.OneCellAnchor(from, new Xdr.Extent { Cx = 100, Cy = 100 }, picture, new Xdr.ClientData()));
                }
            }
        }
        return stream.ToArray();
    }

    private void Set(string reference, Cell cell)
    {
        var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var row = int.Parse(reference[column.Length..], CultureInfo.InvariantCulture);
        if (!rows.TryGetValue(row, out var cells)) rows.Add(row, cells = []);
        cell.CellReference = reference;
        cells[column] = cell;
    }

    private static int ColumnNumber(string column) => column.Aggregate(0, (value, character) => value * 26 + character - 'A' + 1);
}
