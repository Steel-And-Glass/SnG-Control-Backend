using SnG.Control.Domain.Enums;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace SnG.Control.Infrastructure.Quotations;

public sealed class XlsxBudgetQuotationParser(IOptions<BudgetQuotationParserOptions> options) : IBudgetQuotationParser
{
    public async Task<BudgetQuotationParseResult> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return new BudgetQuotationParseResult.Invalid(BudgetQuotationFileError.UnsupportedWorkbookFormat);
        try
        {
            using var buffer = new MemoryStream();
            var bytes = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(bytes, cancellationToken)) > 0)
            {
                if (buffer.Length + read > options.Value.MaxUploadBytes)
                    throw new UnsupportedQuotationException(BudgetQuotationFileError.TechnicalLimitExceeded);
                await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
            }
            buffer.Position = 0;
            ValidatePackage(buffer, cancellationToken);
            buffer.Position = 0;
            using var workbook = SpreadsheetDocument.Open(buffer, false, new OpenSettings
            {
                AutoSave = false,
                MaxCharactersInPart = options.Value.MaxUncompressedBytes
            });
            if (workbook.DocumentType != SpreadsheetDocumentType.Workbook || workbook.WorkbookPart?.VbaProjectPart is not null)
                throw new UnsupportedQuotationException();
            return ReadWorkbook(workbook, fileName, cancellationToken);
        }
        catch (UnsupportedQuotationException exception)
        {
            return new BudgetQuotationParseResult.Invalid(exception.Error);
        }
        catch (Exception exception) when (exception is OpenXmlPackageException or InvalidDataException or XmlException
            or FormatException or OverflowException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return new BudgetQuotationParseResult.Invalid(BudgetQuotationFileError.UnreadableWorkbook);
        }
    }

    internal void ValidatePackage(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > options.Value.MaxZipEntries)
            throw new UnsupportedQuotationException(BudgetQuotationFileError.TechnicalLimitExceeded);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declared = 0, actual = 0;
        var bytes = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName) || entry.FullName.StartsWith('/') || entry.FullName.Contains('\\')
                || entry.FullName.Split('/').Any(segment => segment == "..")) throw new UnsupportedQuotationException();
            declared = checked(declared + entry.Length);
            if (declared > options.Value.MaxUncompressedBytes)
                throw new UnsupportedQuotationException(BudgetQuotationFileError.TechnicalLimitExceeded);
            using var content = entry.Open();
            int read;
            while ((read = content.Read(bytes)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                actual = checked(actual + read);
                if (actual > options.Value.MaxUncompressedBytes)
                    throw new UnsupportedQuotationException(BudgetQuotationFileError.TechnicalLimitExceeded);
            }
        }
        if (!names.Contains("[Content_Types].xml")) throw new UnsupportedQuotationException();
    }

    private static BudgetQuotationParseResult ReadWorkbook(SpreadsheetDocument package, string fileName, CancellationToken cancellationToken)
    {
        var workbook = package.WorkbookPart ?? throw new UnsupportedQuotationException();
        var sheets = workbook.Workbook?.Sheets?.Elements<Sheet>().Where(sheet =>
            string.Equals(sheet.Name?.Value?.Trim(), "COTIZACIÓN", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sheets is null || sheets.Length != 1 || sheets[0].Id?.Value is not string sheetId)
            throw new UnsupportedQuotationException();
        if (workbook.GetPartById(sheetId) is not WorksheetPart worksheet) throw new UnsupportedQuotationException();
        var reader = new QuotationCellReader(workbook);
        var rows = QuotationWorksheetLayout.ReadRows(worksheet, cancellationToken);
        Cell? At(int row, string column) => rows.TryGetValue(row, out var cells) ? cells.GetValueOrDefault(column) : null;
        string TextAt(int row, string column) => reader.Text(At(row, column)).Trim();

        var sg = TextAt(10, "O").ToUpperInvariant();
        if (!sg.StartsWith("SG", StringComparison.Ordinal) || sg.Length < 3 || sg.Skip(2).Any(character => character is < '0' or > '9'))
            throw new UnsupportedQuotationException(BudgetQuotationFileError.InvalidDocumentSgCode);
        var safeName = fileName.Replace('\\', '/').Split('/').Last();
        var document = new BudgetQuotationDocument(safeName, TextAt(10, "C"), TextAt(11, "C"), TextAt(12, "C"),
            TextAt(10, "I"), TextAt(11, "I"), TextAt(12, "I"), sg);

        var boundaries = new List<(int Row, bool Header)>();
        foreach (var (row, cells) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Only textual layout markers are inspected here; N/O financial values are never read.
            var markers = cells.Where(pair => pair.Key is not ("N" or "O")
                && (pair.Value.DataType?.Value == CellValues.SharedString || pair.Value.DataType?.Value == CellValues.InlineString || pair.Value.DataType?.Value == CellValues.String)
                && pair.Value.CellFormula is null).Select(pair => Normalize(reader.Text(pair.Value))).ToArray();
            if (markers.Any(text => text is "CONTRATADO" or "MODIFICADO" or "CONTRATADO / MODIFICADO")) throw new UnsupportedQuotationException();
            var additional = markers.Contains("ADICIONALES");
            var header = Normalize(TextAt(row, "A")) is "ÍTEM" or "ITEM";
            if (additional && header) throw new UnsupportedQuotationException();
            if (header && reader.ItemNumber(At(row + 1, "A")).Length > 0)
            {
                var expected = new (string Column, string[] Values)[]
                {
                    ("B", ["UBICACIÓN", "UBICACION"]), ("C", ["CONFIGURACIÓN", "CONFIGURACION"]),
                    ("D", ["DESCRIPCIÓN", "DESCRIPCION"]), ("J", ["AN (MT)"]), ("K", ["AL (MT)"]),
                    ("L", ["M2", "M²"]), ("M", ["CNT"]), ("N", ["VR UNT"]), ("O", ["VR TOTAL"])
                };
                if (expected.Any(entry => !entry.Values.Contains(Normalize(TextAt(row, entry.Column)))))
                    throw new UnsupportedQuotationException();
            }
            if (header || additional) boundaries.Add((row, header));
        }
        var images = ReadImages(worksheet, cancellationToken);
        var items = new List<BudgetQuotationPreviewItem>();
        var warnings = new List<BudgetQuotationWarning>();
        var section = CommercialItemSection.Main;
        for (var index = 0; index < boundaries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boundary = boundaries[index];
            if (!boundary.Header) { section = CommercialItemSection.Additional; continue; }
            var sourceRow = boundary.Row + 1;
            var endRow = index + 1 < boundaries.Count ? boundaries[index + 1].Row : rows.Keys.Last() + 1;
            if (sourceRow >= endRow) throw new UnsupportedQuotationException();
            var number = reader.ItemNumber(At(sourceRow, "A"));
            if (number.Length == 0) continue;

            var configuration = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows.Keys.Where(row => row >= sourceRow && row < endRow))
            {
                var label = Normalize(TextAt(row, "C"));
                if (label is "SISTEMA" or "CRISTAL" or "ACABADO ALUMINIO" or "CERRADURA" or "OBSERVACIONES")
                {
                    if (!configuration.TryAdd(label, TextAt(row, "D"))) throw new UnsupportedQuotationException();
                }
            }
            if (configuration.Count != 5) throw new UnsupportedQuotationException();
            var quantityValue = reader.PositiveDecimal(At(sourceRow, "M"));
            if (quantityValue > int.MaxValue || decimal.Truncate(quantityValue) != quantityValue) throw new UnsupportedQuotationException();
            var matches = images.Where(image => image.Row >= boundary.Row && image.Row < endRow).ToArray();
            BudgetQuotationImage? image = null;
            if (matches.Length == 1)
            {
                using var imageStream = matches[0].Part.GetStream(FileMode.Open, FileAccess.Read);
                using var imageBytes = new MemoryStream();
                imageStream.CopyTo(imageBytes);
                image = new BudgetQuotationImage(matches[0].Part.ContentType, Convert.ToBase64String(imageBytes.ToArray()));
            }
            else
            {
                var code = matches.Length == 0 ? BudgetQuotationWarningCode.MissingImage : BudgetQuotationWarningCode.AmbiguousImage;
                warnings.Add(new BudgetQuotationWarning(code, number, sourceRow, matches.Length == 0
                    ? "El ítem no tiene una imagen asociable." : "Hay varias imágenes en el bloque; no se seleccionó ninguna."));
            }
            items.Add(new BudgetQuotationPreviewItem(number, section, TextAt(sourceRow, "B"), configuration["SISTEMA"],
                configuration["CRISTAL"], configuration["ACABADO ALUMINIO"], configuration["CERRADURA"], configuration["OBSERVACIONES"],
                reader.PositiveDecimal(At(sourceRow, "J")), reader.PositiveDecimal(At(sourceRow, "K")),
                reader.PositiveDecimal(At(sourceRow, "L")), (int)quantityValue, image, sourceRow));
        }
        if (items.Count == 0) throw new UnsupportedQuotationException();
        return new BudgetQuotationParseResult.Success(document, items, warnings, XlsxBudgetQuotationEconomicsReader.Read(workbook, rows, items.Select(i => new BudgetEconomicRow(i.Section, i.SourceRow)).ToArray(), reader));
    }

    private static List<(int Row, ImagePart Part)> ReadImages(WorksheetPart worksheet, CancellationToken cancellationToken)
    {
        var result = new List<(int, ImagePart)>();
        var drawings = worksheet.DrawingsPart;
        if (drawings?.WorksheetDrawing is null) return result;
        foreach (var anchor in drawings.WorksheetDrawing.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pictures = anchor.Descendants<Xdr.Picture>().ToArray();
            if (pictures.Length == 0) continue;
            if (anchor is not (Xdr.OneCellAnchor or Xdr.TwoCellAnchor) || anchor.Elements<Xdr.Picture>().Count() != pictures.Length)
                throw new UnsupportedQuotationException();
            var from = anchor.GetFirstChild<Xdr.FromMarker>();
            if (!int.TryParse(from?.RowId?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row >= 1_048_576)
                throw new UnsupportedQuotationException();
            foreach (var picture in pictures)
            {
                var blips = picture.Descendants<A.Blip>().ToArray();
                if (blips.Length != 1 || blips[0].Link is not null || blips[0].Embed?.Value is not string id
                    || drawings.GetPartById(id) is not ImagePart image) throw new UnsupportedQuotationException();
                result.Add((row + 1, image));
            }
        }
        return result;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
