using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Quotations;

public sealed class XlsxCommercialQuotationParser(IBudgetQuotationParser technical, IOptions<BudgetQuotationParserOptions> options)
    : ICommercialQuotationParser
{
    public async Task<CommercialParseResult> ParseAsync(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(bytes, writable: false);
        // The approved parser validates the complete package and existing limits before the economic read.
        var parsed = await technical.ParseAsync(input, fileName, cancellationToken);
        if (parsed is BudgetQuotationParseResult.Invalid invalid) return new(CommercialQuotationStatus.InvalidWorkbook, FileError: invalid.Error);
        var document = (BudgetQuotationParseResult.Success)parsed;
        try
        {
            using var buffer = new MemoryStream(bytes, writable: false);
            using var package = SpreadsheetDocument.Open(buffer, false, new OpenSettings { AutoSave = false, MaxCharactersInPart = options.Value.MaxUncompressedBytes });
            var workbook = package.WorkbookPart!;
            var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(s => string.Equals(s.Name?.Value?.Trim(), "COTIZACIÓN", StringComparison.OrdinalIgnoreCase));
            var rows = QuotationWorksheetLayout.ReadRows((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!), cancellationToken);
            var reader = new QuotationCellReader(workbook);
            var additional = rows.Where(r => string.Equals(reader.Text(r.Value.GetValueOrDefault("A")).Trim(), "ADICIONALES", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (additional.Length != 1) return new(CommercialQuotationStatus.InvalidWorkbook, FileError: BudgetQuotationFileError.UnsupportedWorkbookFormat);
            var totals = rows.Where(r => r.Key < additional[0].Key
                && string.Equals(reader.Text(r.Value.GetValueOrDefault("N")).Trim(), "VALOR TOTAL", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (totals.Length != 1) return new(CommercialQuotationStatus.InvalidEconomics);
            var monetaryCells = document.Items.SelectMany(i => new[] {
                rows[i.SourceRow].GetValueOrDefault("N"), rows[i.SourceRow].GetValueOrDefault("O")
            }).Append(totals[0].Value.GetValueOrDefault("O")).ToArray();
            var financialStatus = QuotationFinancialCache.Validate(workbook, rows, monetaryCells, reader);
            if (financialStatus != CommercialQuotationStatus.Success) return new(financialStatus);
            var total = Money.Normalize(reader.NonnegativeDecimal(totals[0].Value.GetValueOrDefault("O")));
            var items = document.Items.Select(i => new CommercialParsedItem(new(i.Section, i.SourceRow),
                new CatalogItemValues(i.ItemNumber, i.Location, i.System, i.Glass, i.AluminumFinish, i.LockType, i.Observations, i.Width, i.Height, i.SquareMeters, i.Quantity),
                Money.Normalize(reader.NonnegativeDecimal(rows[i.SourceRow].GetValueOrDefault("N"))), Money.Normalize(reader.NonnegativeDecimal(rows[i.SourceRow].GetValueOrDefault("O"))))).ToArray();
            return new(CommercialQuotationStatus.Success, new(document.Document.FileName, document.Document.SgCode, total, items));
        }
        catch (Exception error) when (error is UnsupportedQuotationException or OpenXmlPackageException or InvalidDataException
            or XmlException or FormatException or OverflowException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return new(CommercialQuotationStatus.InvalidEconomics); }
    }
}
