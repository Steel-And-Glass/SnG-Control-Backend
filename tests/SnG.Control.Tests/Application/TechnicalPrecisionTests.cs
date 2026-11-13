using System.Globalization;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Tests.Application;

public class TechnicalPrecisionTests
{
    // Mirrored in Frontend technical-precision.test.tsx: exact decimal input, scale, expected.
    [Theory]
    [InlineData("2.0019999999999998", 3, "2.002")]
    [InlineData("5.845839999999999", 6, "5.84584")]
    [InlineData("2.0024999999999999", 3, "2.002")]
    [InlineData("2.0025", 3, "2.003")]
    [InlineData("2.0025000000000001", 3, "2.003")]
    [InlineData("5.845840499999999", 6, "5.84584")]
    [InlineData("5.8458405", 6, "5.845841")]
    [InlineData("0.0004999", 3, "0")]
    [InlineData("0.0005", 3, "0.001")]
    [InlineData("0.0000005", 6, "0.000001")]
    [InlineData("-2.0025", 3, "-2.003")]
    [InlineData("99999999999999999999.9995", 3, "100000000000000000000")]
    public void ExactDecimalRounding(string raw, int scale, string expected)
    {
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        var rounded = scale == 3 ? TechnicalMeasurePrecision.Dimension(value) : TechnicalMeasurePrecision.Surface(value);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), rounded);
    }
    [Fact]
    public void ComparisonSuppressesOnlyEquivalentMeasuresAndKeepsOriginals()
    {
        static CatalogItemValues Values(decimal w, decimal h, decimal a, int q = 1) => new("1", "", "", "", "", "", "", w, h, a, q);
        var original = Values(2.002m, 1.001m, 5.84584m);
        var noisy = Values(2.0019999999999998m, 1.0009999999999999m, 5.845839999999999m);
        Assert.Empty(CommercialTechnicalDifferences.Compare(original, noisy));
        Assert.Equal(2.0019999999999998m, noisy.Width); Assert.Equal(5.845839999999999m, noisy.SquareMeters);
        var differences = CommercialTechnicalDifferences.Compare(original, Values(2.0025m, 1.0015m, 5.8458405m, 2));
        Assert.Equal(new[] { TechnicalField.Width, TechnicalField.Height, TechnicalField.SquareMeters, TechnicalField.Quantity }, differences.Select(d => d.Field));
        Assert.Equal(new[] { "2.003", "1.002", "5.845841", "2" }, differences.Select(d => d.CurrentValue));
        Assert.Single(CommercialTechnicalDifferences.Compare(Values(1.000499999m, 1, 1), Values(1.0005m, 1, 1)));
    }
}
