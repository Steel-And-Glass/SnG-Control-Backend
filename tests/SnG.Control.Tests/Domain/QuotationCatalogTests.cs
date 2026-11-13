using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Tests.Domain;

public class QuotationCatalogTests
{
    private static CatalogItemValues Values(string number = "01", decimal width = 1, decimal height = 2,
        decimal area = 7, int quantity = 3) => new(number, null, "  System  inside  ", "", null, null, "", width, height, area, quantity);
    private static BudgetQuotationVersion Version(Guid project, int version = 1) => new(project, version, "folder/anonymous.xlsx", "", "", "", "", "", "", "SG0012");

    [Fact]
    public void Entities_GenerateIdsKeepUtcTimesAndAllowDuplicateVisibleNumbers()
    {
        var project = Guid.NewGuid(); var version = Version(project); var catalog = new CommercialCatalog(version.Id);
        var first = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, 15, Values());
        var second = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Additional, 24, Values());
        var extraction = new CommercialCatalogItemExtraction(first.Id, Values(), CatalogItemImageStatus.Missing, null, null);
        Assert.NotEqual(Guid.Empty, version.Id); Assert.NotEqual(Guid.Empty, catalog.Id); Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(1, version.VersionNumber); Assert.Equal(project, version.ProjectId); Assert.Equal(version.Id, catalog.BudgetQuotationVersionId);
        Assert.Equal(DateTimeKind.Utc, version.ConfirmedAt.Kind); Assert.Equal(DateTimeKind.Utc, catalog.CreatedAt.Kind);
        Assert.Equal(first.ItemNumber, second.ItemNumber); Assert.Equal(first.Id, extraction.CatalogItemId);
        Assert.Equal("anonymous.xlsx", version.OriginalFileName); Assert.False(Path.IsPathRooted(version.OriginalFileStorageKey));
        Assert.DoesNotContain("SG0012", version.OriginalFileStorageKey); Assert.Contains(version.Id.ToString(), version.OriginalFileStorageKey);
        Assert.Equal("System  inside", first.System); Assert.Equal("", first.Location); Assert.Equal(7, first.SquareMeters);
        foreach (var type in new[] { typeof(BudgetQuotationVersion), typeof(CommercialCatalog), typeof(CommercialCatalogItem), typeof(CommercialCatalogItemExtraction) })
            Assert.All(type.GetProperties(), property => Assert.True(property.SetMethod!.IsPrivate));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("version-zero")]
    [InlineData("version-negative")]
    [InlineData("catalog")]
    [InlineData("item-catalog")]
    [InlineData("section")]
    [InlineData("row")]
    [InlineData("extraction-item")]
    [InlineData("number")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("area")]
    [InlineData("quantity")]
    public void InvalidDomainValues_AreRejected(string invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            _ = invalid switch
            {
                "project" => (object)Version(Guid.Empty),
                "version-zero" => Version(Guid.NewGuid(), 0),
                "version-negative" => Version(Guid.NewGuid(), -1),
                "catalog" => new CommercialCatalog(Guid.Empty),
                "item-catalog" => new CommercialCatalogItem(Guid.Empty, CommercialItemSection.Main, 15, Values()),
                "section" => new CommercialCatalogItem(Guid.NewGuid(), (CommercialItemSection)0, 15, Values()),
                "row" => new CommercialCatalogItem(Guid.NewGuid(), CommercialItemSection.Main, 0, Values()),
                "extraction-item" => new CommercialCatalogItemExtraction(Guid.Empty, Values(), CatalogItemImageStatus.Missing, null, null),
                "number" => Values(" "),
                "width" => Values(width: 0),
                "height" => Values(height: -1),
                "area" => Values(area: 0),
                _ => Values(quantity: 0)
            };
        });
    }

    [Theory]
    [InlineData(CatalogItemImageStatus.Stored, "projects/test/image.png", "image/png", true)]
    [InlineData(CatalogItemImageStatus.Missing, null, null, true)]
    [InlineData(CatalogItemImageStatus.Ambiguous, null, null, true)]
    [InlineData(CatalogItemImageStatus.Stored, null, "image/png", false)]
    [InlineData(CatalogItemImageStatus.Stored, "projects/image.png", null, false)]
    [InlineData(CatalogItemImageStatus.Missing, "projects/image.png", "image/png", false)]
    [InlineData(CatalogItemImageStatus.Ambiguous, null, "image/png", false)]
    [InlineData(CatalogItemImageStatus.Stored, "../image.png", "image/png", false)]
    [InlineData((CatalogItemImageStatus)99, null, null, false)]
    public void ImageState_IsConsistent(CatalogItemImageStatus status, string? key, string? contentType, bool valid)
    {
        CommercialCatalogItemExtraction Create() => new(Guid.NewGuid(), Values(), status, key, contentType);
        if (valid) { var result = Create(); Assert.Equal(status, result.ImageStatus); Assert.Equal(key, result.ImageStorageKey); }
        else Assert.ThrowsAny<ArgumentException>(Create);
    }
}
