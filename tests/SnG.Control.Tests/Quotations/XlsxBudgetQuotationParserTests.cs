using SnG.Control.Domain.Enums;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public class XlsxBudgetQuotationParserTests
{
    [Fact]
    public async Task StandardWorkbook_ExtractsMetadataTechnicalValuesLabelsAndOriginalImageOnly()
    {
        var bytes = new QuotationWorkbook(" sg0012 ").Item().Build();
        var original = bytes.ToArray();
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(bytes, "C:\\uploads\\synthetic.xlsx"));
        Assert.Equal(new BudgetQuotationDocument("synthetic.xlsx", "Cliente sintético", "Proyecto sintético", "Ubicación sintética", "Línea cliente", "Línea producción", "Equipo de pruebas", "SG0012"), result.Document);
        var item = Assert.Single(result.Items);
        Assert.Equal("01", item.ItemNumber); Assert.Equal(15, item.SourceRow); Assert.Equal(CommercialItemSection.Main, item.Section);
        Assert.Equal("Zona sintética", item.Location); Assert.Equal("Sistema prueba", item.System); Assert.Equal("Vidrio prueba", item.Glass);
        Assert.Equal("Acabado prueba", item.AluminumFinish); Assert.Equal("Cierre prueba", item.LockType); Assert.Equal("", item.Observations);
        Assert.Equal(1m, item.Width); Assert.Equal(2.8m, item.Height); Assert.Equal(5.6m, item.SquareMeters); Assert.Equal(2, item.Quantity);
        Assert.Equal("image/png", item.Image!.ContentType); Assert.Equal(QuotationWorkbook.FirstImage, Convert.FromBase64String(item.Image.Base64));
        Assert.Empty(result.Warnings); Assert.Equal(original, bytes);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("123456", json); Assert.DoesNotContain("246912", json); Assert.DoesNotContain("NO EXPORTAR", json);
        Assert.DoesNotContain(typeof(BudgetQuotationPreviewItem).GetProperties(), property =>
            new[] { "Price", "Cost", "Profit", "Protection", "TotalPrice", "UnitPrice" }.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task SectionsRepeatedNumbersAndDocumentOrder_ArePreservedAndEmptyTemplatesIgnored()
    {
        var book = new QuotationWorkbook().Item(14, "26").Item(22, "28").Item(30, "27").Item(39, "28", additional: true)
            .Item(47, "", image: false).Text("J47", "AN").Text("K47", "AL");
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(book.Build()));
        Assert.Equal(new[] { "26", "28", "27", "28" }, result.Items.Select(item => item.ItemNumber));
        Assert.Equal(new[] { 15, 23, 31, 40 }, result.Items.Select(item => item.SourceRow));
        Assert.Equal(3, result.Items.Count(item => item.Section == CommercialItemSection.Main));
        Assert.Equal(CommercialItemSection.Additional, result.Items.Last().Section);
    }

    [Fact]
    public async Task AdditionalHeadingWithoutRealItems_DoesNotCreateAnAdditional()
    {
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(new QuotationWorkbook().Item().Item(23, "", image: false, additional: true).Build()));
        Assert.Equal(CommercialItemSection.Main, Assert.Single(result.Items).Section);
    }

    [Fact]
    public async Task NumericItemWithZeroFormat_PreservesVisibleLeadingZero()
    {
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(new QuotationWorkbook().Item().Number("A15", 1, 1).Build()));
        Assert.Equal("01", Assert.Single(result.Items).ItemNumber);
    }

    [Fact]
    public async Task CachedFormula_IsReadWithoutCalculationAndM2IsNotReconstructed()
    {
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(new QuotationWorkbook().Item()
            .Formula("L15", "J15*K15*M15", "7.125").Formula("J15", "[remote.xlsx]Sheet1!A1", "1.25").Build()));
        Assert.Equal(7.125m, result.Items[0].SquareMeters); Assert.Equal(1.25m, result.Items[0].Width);
    }

    [Fact]
    public async Task ImageAnchorsWinOverPackageOrder_AndLogoOutsideBlocksIsIgnored()
    {
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(new QuotationWorkbook().Item(14, "1", image: false)
            .Item(22, "2", image: false).Image(2).Image(15, QuotationWorkbook.FirstImage, twoCell: true).Image(23, QuotationWorkbook.SecondImage).Build()));
        Assert.Equal(QuotationWorkbook.FirstImage, Convert.FromBase64String(result.Items[0].Image!.Base64));
        Assert.Equal(QuotationWorkbook.SecondImage, Convert.FromBase64String(result.Items[1].Image!.Base64));
    }

    [Theory]
    [InlineData(false, BudgetQuotationWarningCode.MissingImage)]
    [InlineData(true, BudgetQuotationWarningCode.AmbiguousImage)]
    public async Task MissingOrAmbiguousImage_ProducesStructuredWarningWithoutSelectingImage(bool ambiguous, BudgetQuotationWarningCode code)
    {
        var book = new QuotationWorkbook().Item(image: false);
        if (ambiguous) book.Image(15).Image(16, QuotationWorkbook.SecondImage);
        var result = Assert.IsType<BudgetQuotationParseResult.Success>(await Parse(book.Build()));
        Assert.Null(result.Items[0].Image);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(code, warning.Code); Assert.Equal("01", warning.ItemNumber); Assert.Equal(15, warning.SourceRow);
    }

    [Theory]
    [InlineData("file.xls")]
    [InlineData("file.csv")]
    [InlineData("file.xlsm")]
    public async Task WrongExtension_IsRejectedEvenWithXlsxContent(string name) =>
        Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parse(new QuotationWorkbook().Item().Build(), name));

    [Theory]
    [InlineData("invalid")]
    [InlineData("empty")]
    [InlineData("ole-encrypted")]
    public async Task NotReadableWorkbook_ReturnsControlledError(string kind)
    {
        byte[] bytes = kind switch { "empty" => [], "ole-encrypted" => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], _ => Encoding.UTF8.GetBytes("not an xlsx") };
        Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parse(bytes));
    }

    [Theory]
    [InlineData("missing-sheet")]
    [InlineData("no-items")]
    [InlineData("incomplete-header")]
    [InlineData("missing-width")]
    [InlineData("zero-quantity")]
    [InlineData("fractional-quantity")]
    [InlineData("text-number")]
    [InlineData("negative-height")]
    [InlineData("missing-cache")]
    [InlineData("contratado")]
    [InlineData("modificado")]
    [InlineData("missing-label")]
    [InlineData("duplicate-label")]
    [InlineData("unknown-number-format")]
    [InlineData("macro-content")]
    public async Task UnsupportedStructuresOrRequiredValues_AreRejected(string scenario)
    {
        var book = new QuotationWorkbook().Item();
        switch (scenario)
        {
            case "missing-sheet": book.SheetName = "COTIZACION"; break;
            case "no-items": book.Text("A15", ""); break;
            case "incomplete-header": book.Text("K14", "Unknown"); break;
            case "missing-width": book.Text("J15", ""); break;
            case "zero-quantity": book.Number("M15", 0); break;
            case "fractional-quantity": book.Number("M15", 1.5m); break;
            case "text-number": book.Text("J15", "1,5"); break;
            case "negative-height": book.Number("K15", -1); break;
            case "missing-cache": book.Formula("L15", "J15*K15*M15", null); break;
            case "contratado": book.Text("B13", "CONTRATADO"); break;
            case "modificado": book.Text("B13", "MODIFICADO"); break;
            case "missing-label": book.Text("C16", "SISTEM"); break;
            case "duplicate-label": book.Text("C19", "SISTEMA"); break;
            case "unknown-number-format": book.Number("A15", 1, 2); break;
            case "macro-content": book.DocumentType = SpreadsheetDocumentType.MacroEnabledWorkbook; break;
        }
        Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parse(book.Build()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SG")]
    [InlineData("SG-12")]
    [InlineData("SG 12")]
    [InlineData("SG١٢")]
    public async Task InvalidDocumentSg_IsRejected(string sg)
    {
        var result = Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parse(new QuotationWorkbook(sg).Item().Build()));
        Assert.Equal(BudgetQuotationFileError.InvalidDocumentSgCode, result.Error);
    }

    [Fact]
    public async Task NonSeekableStream_IsSupportedAndLeftOpen()
    {
        using var memory = new MemoryStream(new QuotationWorkbook().Item().Build());
        using var input = new NonSeekableStream(memory);
        Assert.IsType<BudgetQuotationParseResult.Success>(await Parser().ParseAsync(input, "file.xlsx", CancellationToken.None));
        Assert.True(memory.CanRead);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("expanded")]
    [InlineData("entries")]
    public async Task ConfigurableTechnicalLimits_ReturnControlledError(string kind)
    {
        var options = new BudgetQuotationParserOptions();
        if (kind == "upload") options.MaxUploadBytes = 1;
        if (kind == "expanded") options.MaxUncompressedBytes = 1;
        if (kind == "entries") options.MaxZipEntries = 1;
        using var input = new MemoryStream(new QuotationWorkbook().Item().Build());
        var result = Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parser(options).ParseAsync(input, "file.xlsx", CancellationToken.None));
        Assert.Equal(BudgetQuotationFileError.TechnicalLimitExceeded, result.Error);
    }

    [Fact]
    public async Task ZipTraversalEntry_IsRejectedWithoutExtraction()
    {
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        { using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); writer.Write("test"); }
        input.Position = 0;
        Assert.IsType<BudgetQuotationParseResult.Invalid>(await Parser().ParseAsync(input, "file.xlsx", CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsNotConvertedToInvalidWorkbook()
    {
        using var input = new MemoryStream(new QuotationWorkbook().Item().Build());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Parser().ParseAsync(input, "file.xlsx", cancellation.Token));
    }

    private static XlsxBudgetQuotationParser Parser(BudgetQuotationParserOptions? options = null) => new(Options.Create(options ?? new()));
    private static async Task<BudgetQuotationParseResult> Parse(byte[] bytes, string name = "synthetic.xlsx")
    { using var stream = new MemoryStream(bytes); return await Parser().ParseAsync(stream, name, CancellationToken.None); }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
