using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Domain;

public class ProjectTests
{
    [Theory]
    [InlineData(ProductLine.Classic)]
    [InlineData(ProductLine.Essential)]
    [InlineData(ProductLine.Bioconfort)]
    [InlineData(ProductLine.Signature)]
    public void Constructor_WithValidArguments_CreatesProjectWithUtcTimestamps(ProductLine productLine)
    {
        var clientId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var project = new Project(clientId, "Torre Norte", "Bogotá", productLine, "google-test-subject");

        var after = DateTime.UtcNow;
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(clientId, project.ClientId);
        Assert.Equal("Torre Norte", project.Name);
        Assert.Equal("Bogotá", project.City);
        Assert.Equal(productLine, project.ProductLine);
        Assert.Null(project.SgCode);
        Assert.Equal(DateTimeKind.Utc, project.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, project.UpdatedAt.Kind);
        Assert.InRange(project.CreatedAt, before, after);
        Assert.Equal(project.CreatedAt, project.UpdatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyClientId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("clientId",
            () => new Project(Guid.Empty, "Torre Norte", "Bogotá", ProductLine.Classic, "google-test-subject"));
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("name",
            () => new Project(Guid.NewGuid(), null!, "Bogotá", ProductLine.Classic, "google-test-subject"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Constructor_WithBlankName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>("name",
            () => new Project(Guid.NewGuid(), name, "Bogotá", ProductLine.Classic, "google-test-subject"));
    }

    [Fact]
    public void Constructor_WithNullCity_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("city",
            () => new Project(Guid.NewGuid(), "Torre Norte", null!, ProductLine.Classic, "google-test-subject"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Constructor_WithBlankCity_ThrowsArgumentException(string city)
    {
        Assert.Throws<ArgumentException>("city",
            () => new Project(Guid.NewGuid(), "Torre Norte", city, ProductLine.Classic, "google-test-subject"));
    }

    [Fact]
    public void Constructor_TrimsNameAndCityAndPreservesInternalWhitespace()
    {
        var project = new Project(Guid.NewGuid(), " \tTorre  Norte\r\n", "\tSanta  Marta  ", ProductLine.Classic, "google-test-subject");

        Assert.Equal("Torre  Norte", project.Name);
        Assert.Equal("Santa  Marta", project.City);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void Constructor_WithUndefinedProductLine_ThrowsArgumentOutOfRangeException(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>("productLine",
            () => new Project(Guid.NewGuid(), "Torre Norte", "Bogotá", (ProductLine)value, "google-test-subject"));

        Assert.Equal((ProductLine)value, exception.ActualValue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithRepeatedName_AllowsProjectsWithDistinctIdentities(bool sameClient)
    {
        var firstClient = new Client("Cliente A");
        var secondClient = sameClient ? firstClient : new Client("Cliente B");

        var first = new Project(firstClient.Id, "Torre Norte", "Bogotá", ProductLine.Classic, "google-test-subject");
        var second = new Project(secondClient.Id, "Torre Norte", "Bogotá", ProductLine.Classic, "google-test-subject");

        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(firstClient.Id, first.ClientId);
        Assert.Equal(secondClient.Id, second.ClientId);
    }
}
