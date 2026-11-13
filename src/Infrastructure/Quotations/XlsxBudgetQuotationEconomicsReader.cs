using System.Globalization;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Infrastructure.Quotations;

// Original-file economics only. No image extraction, formula evaluation or commercial snapshot lookup.
public sealed class XlsxBudgetQuotationEconomicsReader(IOptions<BudgetQuotationParserOptions> options) : IBudgetQuotationEconomicsReader
{
    public async Task<BudgetQuotationEconomics> ReadAsync(Stream original, IReadOnlyList<BudgetEconomicRow> items, CancellationToken token)
    {
        try
        {
            using var buffer = new MemoryStream(); var block = new byte[81920]; int length;
            while ((length = await original.ReadAsync(block, token)) > 0)
            {
                if (buffer.Length + length > options.Value.MaxUploadBytes) return BudgetQuotationEconomics.Unavailable("InvalidEconomics");
                await buffer.WriteAsync(block.AsMemory(0, length), token);
            }
            buffer.Position = 0; new XlsxBudgetQuotationParser(options).ValidatePackage(buffer, token); buffer.Position = 0;
            using var package = SpreadsheetDocument.Open(buffer, false, new OpenSettings { AutoSave = false, MaxCharactersInPart = options.Value.MaxUncompressedBytes });
            var workbook = package.WorkbookPart ?? throw new UnsupportedQuotationException();
            var sheets = workbook.Workbook?.Sheets ?? throw new UnsupportedQuotationException();
            var sheet = sheets.Elements<Sheet>().Single(s => string.Equals(s.Name?.Value?.Trim(), "COTIZACIÓN", StringComparison.OrdinalIgnoreCase));
            var rows = QuotationWorksheetLayout.ReadRows((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!), token);
            return Read(workbook, rows, items, new QuotationCellReader(workbook));
        }
        catch (Exception e) when (e is UnsupportedQuotationException or OpenXmlPackageException or InvalidDataException or XmlException
            or FormatException or OverflowException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return BudgetQuotationEconomics.Unavailable("InvalidEconomics"); }
    }
    internal static BudgetQuotationEconomics Read(WorkbookPart workbook, IReadOnlyDictionary<int, Dictionary<string, Cell>> rows,
        IReadOnlyList<BudgetEconomicRow> items, QuotationCellReader reader)
    {
        try
        {
            var additional = rows.Where(r => string.Equals(reader.Text(r.Value.GetValueOrDefault("A")).Trim(), "ADICIONALES", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (additional.Length != 1 || items.Count == 0 || items.Select(i => i.SourceRow).Distinct().Count() != items.Count) return BudgetQuotationEconomics.Unavailable("InvalidEconomics");
            var totals = rows.Where(r => r.Key < additional[0].Key && string.Equals(reader.Text(r.Value.GetValueOrDefault("N")).Trim(), "VALOR TOTAL", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (totals.Length != 1) return BudgetQuotationEconomics.Unavailable("InvalidEconomics");
            var cells = items.SelectMany(i => new[] { rows[i.SourceRow].GetValueOrDefault("N"), rows[i.SourceRow].GetValueOrDefault("O") }).Append(totals[0].Value.GetValueOrDefault("O")).ToArray();
            var status = QuotationFinancialCache.Validate(workbook, rows, cells, reader);
            if (status != CommercialQuotationStatus.Success) return BudgetQuotationEconomics.Unavailable(status.ToString());
            decimal Amount(int row, string col) => Money.Normalize(reader.NonnegativeDecimal(rows[row].GetValueOrDefault(col)));
            string Text(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
            var total = Amount(totals[0].Key, "O");
            var amounts = items.Select(i => new BudgetEconomicItem(i.Section, i.SourceRow, Text(Amount(i.SourceRow, "N")), Text(Amount(i.SourceRow, "O")))).ToArray();
            // Diagnostic only: document totals may include other concepts. Never replace the saved total.
            var subtotal = items.Where(i => i.Section == CommercialItemSection.Main).Sum(i => Amount(i.SourceRow, "O"));
            return new("Success", Text(total), $"O{totals[0].Key}", amounts, subtotal != total);
        }
        catch (Exception e) when (e is UnsupportedQuotationException or FormatException or OverflowException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return BudgetQuotationEconomics.Unavailable("InvalidEconomics"); }
    }
}
