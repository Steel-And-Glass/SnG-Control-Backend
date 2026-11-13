using System.Globalization;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Quotations;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("5513429.138030602", "5513429.14")]
    [InlineData("48609963.17607693", "48609963.18")]
    [InlineData("98765432.167777", "98765432.17")]
    [InlineData("0", "0.00")]
    [InlineData("1.234", "1.23")]
    [InlineData("1.235", "1.24")]
    [InlineData("1.225", "1.23")]
    [InlineData("999999999999999999.99", "999999999999999999.99")]
    public async Task ParserAndDomainShareMonetaryPolicy(string input, string output)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        var expected = decimal.Parse(output, CultureInfo.InvariantCulture);
        Assert.Equal(expected, Money.Normalize(value));
        var quotation = new CommercialQuotation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "synthetic.xlsx", value, "synthetic", null, null);
        var item = new CommercialQuotationItem(quotation.Id, Guid.NewGuid(), CommercialItemSection.Main, 15,
            new("V5", "Room", "System", "Glass", "Finish", "Lock", "Notes", 1.1234m, 2.5678m, 11.074m, 3), value, value);
        Assert.Equal(expected, quotation.ProjectTotal); Assert.Equal(expected, item.UnitPrice); Assert.Equal(expected, item.TotalPrice);
        Assert.Equal(11.074m, item.SquareMeters); Assert.Equal(1.1234m, item.Width); Assert.Equal(2.5678m, item.Height);
        var bytes = CommercialQuotationScenario.Workbook().Number("N15", value).Number("O15", value).Number("O30", value).Build();
        var parsed = await CommercialQuotationScenario.Parser().ParseAsync(bytes, "synthetic.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, parsed.Status);
        Assert.Equal(expected, parsed.Quotation!.ProjectTotal);
        Assert.Equal(expected, parsed.Quotation.Items[0].UnitPrice); Assert.Equal(expected, parsed.Quotation.Items[0].TotalPrice);
    }

    [Theory]
    [InlineData("-0.001")]
    [InlineData("1000000000000000000")]
    [InlineData("999999999999999999.995")]
    public async Task InvalidMoneyFailsBeforePersistence(string input)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Normalize(value));
        foreach (var cell in new[] { "N15", "O15", "O30" })
        {
            var parsed = await CommercialQuotationScenario.Parser().ParseAsync(
                CommercialQuotationScenario.Workbook().Number(cell, value).Build(), "synthetic.xlsx", default);
            Assert.Equal(CommercialQuotationStatus.InvalidEconomics, parsed.Status);
        }
    }
}
