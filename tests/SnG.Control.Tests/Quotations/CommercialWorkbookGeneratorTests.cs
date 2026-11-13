using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public class CommercialWorkbookGeneratorTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("ItemNumber")]
    [InlineData("Location")]
    [InlineData("System")]
    [InlineData("Glass")]
    [InlineData("AluminumFinish")]
    [InlineData("LockType")]
    [InlineData("Observations")]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("SquareMeters")]
    [InlineData("Quantity")]
    [InlineData("all")]
    public async Task WritesExactlyConfirmedTechnicalValuesInBothSections(string field)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync();
        var items = scenario.State!.WorkbookItems().Select(item => item with { Confirmed = Change(item.Extracted, field) }).Reverse().ToArray();
        var output = await CommercialHandoffScenario.Generator().GenerateAsync(scenario.Original, "SG0012", items, default);
        Assert.NotNull(output);
        using var package = SpreadsheetDocument.Open(new MemoryStream(output), false);
        var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
        Assert.Equal(2, items.Length); Assert.Equal(2, items.Select(item => item.Section).Distinct().Count());
        foreach (var item in items)
        {
            var row = item.SourceRow; var expected = item.Confirmed;
            Assert.Equal(expected.ItemNumber, Text(cells[$"A{row}"])); Assert.Equal(expected.Location, Text(cells[$"B{row}"]));
            Assert.Equal(expected.Glass, Text(cells[$"D{row}"])); Assert.Equal(expected.System, Text(cells[$"D{row + 1}"]));
            Assert.Equal(expected.Observations, Text(cells[$"D{row + 2}"])); Assert.Equal(expected.LockType, Text(cells[$"D{row + 3}"]));
            Assert.Equal(expected.AluminumFinish, Text(cells[$"D{row + 5}"]));
            Assert.Equal(expected.Width, Number(cells[$"J{row}"])); Assert.Equal(expected.Height, Number(cells[$"K{row}"]));
            Assert.Equal(expected.SquareMeters, Number(cells[$"L{row}"])); Assert.Equal(expected.Quantity, Number(cells[$"M{row}"]));
        }
    }

    [Fact]
    public async Task PreservesPackageStructureImagesSharedStringsAndFormulas()
    {
        var original = Enrich(new QuotationWorkbook().Item(image: false).Image(15, twoCell: true)
            .Item(23, additional: true).Formula("L15", "J15*K15*M15", "5.6").Formula("O15", "N15*M15", "246912").Build());
        var immutable = original.ToArray();
        var scenario = await CommercialHandoffScenario.CreateAsync(original);
        var items = scenario.State!.WorkbookItems().Select(item => item with { Confirmed = Change(item.Extracted, "all") }).ToArray();
        var output = await CommercialHandoffScenario.Generator().GenerateAsync(original, "SG0012", items, default);
        Assert.NotNull(output); Assert.Equal(immutable, original);
        using var before = SpreadsheetDocument.Open(new MemoryStream(original), false);
        using var after = SpreadsheetDocument.Open(new MemoryStream(output), false);
        var left = before.WorkbookPart!; var right = after.WorkbookPart!;
        Assert.Equal(left.Workbook!.Sheets!.OuterXml, right.Workbook!.Sheets!.OuterXml);
        Assert.Equal(left.Workbook.DefinedNames!.OuterXml, right.Workbook.DefinedNames!.OuterXml);
        var oldSheet = left.WorksheetParts.Single(part => part.DrawingsPart is not null).Worksheet!;
        var newSheet = right.WorksheetParts.Single(part => part.DrawingsPart is not null).Worksheet!;
        Assert.Equal(oldSheet.GetFirstChild<MergeCells>()!.OuterXml, newSheet.GetFirstChild<MergeCells>()!.OuterXml);
        Assert.Equal(oldSheet.GetFirstChild<Columns>()!.OuterXml, newSheet.GetFirstChild<Columns>()!.OuterXml);
        Assert.Equal(oldSheet.GetFirstChild<SheetDimension>()!.OuterXml, newSheet.GetFirstChild<SheetDimension>()!.OuterXml);
        Assert.Equal(oldSheet.Descendants<Row>().Select(row => (row.RowIndex?.Value, row.Height?.Value, row.CustomHeight?.Value)),
            newSheet.Descendants<Row>().Select(row => (row.RowIndex?.Value, row.Height?.Value, row.CustomHeight?.Value)));
        var oldCells = oldSheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
        var newCells = newSheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
        foreach (var reference in new[] { "C10", "C11", "C12", "I10", "I11", "I12", "O10", "N15", "O15", "N24", "O24", "D19", "D28", "F11", "F17" })
            Assert.Equal(oldCells[reference].OuterXml, newCells[reference].OuterXml);
        foreach (var reference in oldCells.Keys)
        {
            Assert.Equal(oldCells[reference].CellFormula?.OuterXml, newCells[reference].CellFormula?.OuterXml);
            Assert.Equal(oldCells[reference].StyleIndex?.Value, newCells[reference].StyleIndex?.Value);
        }
        Assert.Equal("J15*K15*M15", newCells["L15"].CellFormula!.Text); Assert.Equal(9.5m, Number(newCells["L15"]));
        Assert.Equal("Zona cambiada", Text(newCells["B15"]));
        Assert.Equal(CellValues.SharedString, newCells["F11"].DataType!.Value);
        Assert.Equal(left.SharedStringTablePart!.SharedStringTable!.OuterXml, right.SharedStringTablePart!.SharedStringTable!.OuterXml);
        Assert.Equal(CalculateModeValues.Auto, right.Workbook.CalculationProperties!.CalculationMode!.Value);
        Assert.True(right.Workbook.CalculationProperties.FullCalculationOnLoad!.Value);
        Assert.True(right.Workbook.CalculationProperties.ForceFullCalculation!.Value);
        using var oldZip = new ZipArchive(new MemoryStream(original)); using var newZip = new ZipArchive(new MemoryStream(output));
        var changed = new[] { left.Uri.OriginalString.TrimStart('/'), left.WorksheetParts.Single(part => part.DrawingsPart is not null).Uri.OriginalString.TrimStart('/') };
        Assert.Equal(oldZip.Entries.Select(entry => entry.FullName).Order(), newZip.Entries.Select(entry => entry.FullName).Order());
        Assert.Contains(oldZip.Entries, entry => entry.FullName.Contains("media/"));
        Assert.Contains(oldZip.Entries, entry => entry.FullName.Contains("drawings/"));
        foreach (var entry in oldZip.Entries.Where(entry => !changed.Contains(entry.FullName)))
        {
            using var leftBytes = new MemoryStream(); using var rightBytes = new MemoryStream();
            entry.Open().CopyTo(leftBytes); newZip.GetEntry(entry.FullName)!.Open().CopyTo(rightBytes);
            Assert.Equal(leftBytes.ToArray(), rightBytes.ToArray());
        }
    }

    [Theory]
    [InlineData("row")]
    [InlineData("section")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extraction")]
    [InlineData("sg")]
    [InlineData("corrupt")]
    public async Task RejectsInconsistentMappingBeforeProducingFile(string change)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var items = scenario.State!.WorkbookItems().ToArray();
        if (change == "row") items[0] = items[0] with { SourceRow = 16 };
        if (change == "section") items[0] = items[0] with { Section = CommercialItemSection.Additional };
        if (change == "missing") items = [items[0]];
        if (change == "duplicate") items = [items[0], items[0]];
        if (change == "extraction") items[0] = items[0] with { Extracted = Change(items[0].Extracted, "Width") };
        Assert.Null(await CommercialHandoffScenario.Generator().GenerateAsync(change == "corrupt" ? new byte[] { 0 } : scenario.Original,
            change == "sg" ? "SG12" : "SG0012", items, default));
    }

    [Fact]
    public async Task TextFormula_KeepsFormulaAndUpdatesStringCache()
    {
        var bytes = new QuotationWorkbook().Item().Formula("B15", "\"Zona\"", "Zona").Build();
        var scenario = await CommercialHandoffScenario.CreateAsync(bytes);
        var items = scenario.State!.WorkbookItems().Select(item => item with { Confirmed = Change(item.Extracted, "Location") }).ToArray();
        var output = await CommercialHandoffScenario.Generator().GenerateAsync(bytes, "SG0012", items, default);
        Assert.NotNull(output); using var package = SpreadsheetDocument.Open(new MemoryStream(output), false);
        var cell = package.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().Single(cell => cell.CellReference == "B15");
        Assert.Equal("\"Zona\"", cell.CellFormula!.Text); Assert.Equal(CellValues.String, cell.DataType!.Value); Assert.Equal("Zona cambiada", cell.CellValue!.Text);
    }

    private static CatalogItemValues Change(CatalogItemValues value, string field)
    {
        bool Edit(string name) => field == name || field == "all";
        return new(Edit("ItemNumber") ? "02-C" : value.ItemNumber, Edit("Location") ? "Zona cambiada" : value.Location,
            Edit("System") ? "Sistema nuevo" : value.System, Edit("Glass") ? "Cristal nuevo" : value.Glass,
            Edit("AluminumFinish") ? "Acabado nuevo" : value.AluminumFinish, Edit("LockType") ? "Cerradura nueva" : value.LockType,
            Edit("Observations") ? "Observación nueva" : value.Observations, Edit("Width") ? 3.25m : value.Width,
            Edit("Height") ? 4.75m : value.Height, Edit("SquareMeters") ? 9.5m : value.SquareMeters, Edit("Quantity") ? 7 : value.Quantity);
    }
    private static string Text(Cell cell) => cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? "";
    private static decimal Number(Cell cell) => decimal.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture);
    private static byte[] Enrich(byte[] bytes)
    {
        using var buffer = new MemoryStream(); buffer.Write(bytes); buffer.Position = 0;
        using (var package = SpreadsheetDocument.Open(buffer, true))
        {
            var part = package.WorkbookPart!; var sheet = part.WorksheetParts.Single().Worksheet!;
            sheet.PrependChild(new Columns(new Column { Min = 1, Max = 15, Width = 20, CustomWidth = true }));
            sheet.PrependChild(new SheetDimension { Reference = "A1:O30" });
            sheet.InsertAfter(new MergeCells(new MergeCell { Reference = "D10:H10" }), sheet.GetFirstChild<SheetData>());
            var row = sheet.Descendants<Row>().Single(row => row.RowIndex!.Value == 15); row.Height = 34; row.CustomHeight = true;
            sheet.Descendants<Cell>().Single(cell => cell.CellReference == "J15").StyleIndex = 1;
            var strings = part.AddNewPart<SharedStringTablePart>(); strings.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("Zona sintética")));
            var location = row.Elements<Cell>().Single(cell => cell.CellReference == "B15"); location.InlineString = null; location.DataType = CellValues.SharedString; location.CellValue = new CellValue("0");
            var other = new Cell { CellReference = "F11", DataType = CellValues.SharedString, CellValue = new CellValue("0") };
            var row11 = sheet.Descendants<Row>().Single(row => row.RowIndex!.Value == 11);
            row11.InsertBefore(other, row11.Elements<Cell>().Single(cell => cell.CellReference == "I11"));
            sheet.Descendants<Row>().Single(row => row.RowIndex!.Value == 17).Append(
                new Cell { CellReference = "F17", DataType = CellValues.Error, CellValue = new CellValue("#DIV/0!") });
            var extra = part.AddNewPart<WorksheetPart>(); extra.Worksheet = new Worksheet(new SheetData());
            part.Workbook!.Sheets!.Append(new Sheet { Id = part.GetIdOfPart(extra), SheetId = 2, Name = "Auxiliar" });
            part.Workbook.Append(new DefinedNames(new DefinedName("' COTIZACIÓN '!$J$15") { Name = "Ancho" }));
        }
        return buffer.ToArray();
    }
}
