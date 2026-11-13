using SnG.Control.Application.Files;
namespace SnG.Control.Tests.Application;
public sealed class QuotationDownloadNameTests
{
    [Theory]
    [InlineData("SG 1004 propuesta.xlsx", "SG 1004 propuesta.xlsx")]
    [InlineData("Cotización técnica.xlsx", "Cotización técnica.xlsx")]
    [InlineData("../../carpeta/propuesta.xlsx", "propuesta.xlsx")]
    [InlineData("C:\\carpeta\\propuesta.xlsx", "propuesta.xlsx")]
    [InlineData("archivo?:*.xlsx", "archivo___.xlsx")]
    [InlineData("CON.xlsx", "_CON.xlsx")]
    [InlineData(" .propuesta.xlsx. ", "propuesta.xlsx")]
    public void KeepsOriginalIdentityAndExtensionWithPortableSafeCharacters(string input, string expected) =>
        Assert.Equal(expected, QuotationDownloadName.Sanitize(input));
    [Fact]
    public void BoundsLengthWithoutLosingExtension() =>
        Assert.Equal(new string('x', 195) + ".xlsx", QuotationDownloadName.Sanitize(new string('x', 250) + ".xlsx"));
}
