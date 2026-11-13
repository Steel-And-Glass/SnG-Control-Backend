using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Domain;

public class ProjectSgCodeTests
{
    [Theory]
    [InlineData("SG1", "SG1")]
    [InlineData("SG001", "SG001")]
    [InlineData("SG0012", "SG0012")]
    [InlineData("SG1043", "SG1043")]
    [InlineData("SG25000", "SG25000")]
    [InlineData("sg1043", "SG1043")]
    [InlineData("Sg1043", "SG1043")]
    [InlineData("  sg0012  ", "SG0012")]
    public void SetSgCode_NormalizesAndPreservesIdentityAndCreatedAt(string input, string expected)
    {
        var project = NewProject();
        var identity = (project.Id, project.ClientId, project.Name, project.City, project.ProductLine, project.CreatedAt);
        var before = DateTime.UtcNow;
        project.SetSgCode(input);
        Assert.Equal(expected, project.SgCode);
        Assert.InRange(project.UpdatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, project.UpdatedAt.Kind);
        Assert.Equal(identity, (project.Id, project.ClientId, project.Name, project.City, project.ProductLine, project.CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("1043")]
    [InlineData("S1043")]
    [InlineData("SG")]
    [InlineData("SG-1043")]
    [InlineData("SG 1043")]
    [InlineData("SG1043A")]
    [InlineData("SG12.3")]
    [InlineData("SG١٢")]
    [InlineData("SG１２")]
    [InlineData("SG12\n3")]
    public void SetSgCode_InvalidInput_DoesNotChangeProject(string? input)
    {
        var project = NewProject();
        project.SetSgCode("SG1");
        var updated = project.UpdatedAt;
        Assert.ThrowsAny<ArgumentException>(() => project.SetSgCode(input!));
        Assert.Equal("SG1", project.SgCode);
        Assert.Equal(updated, project.UpdatedAt);
    }

    [Fact]
    public void SetSgCode_CorrectionUpdatesTimestamp_AndNormalizedRepeatIsIdempotent()
    {
        var project = NewProject();
        project.SetSgCode("SG1");
        var before = DateTime.UtcNow;
        project.SetSgCode("sg0042");
        Assert.InRange(project.UpdatedAt, before, DateTime.UtcNow);
        var updated = project.UpdatedAt;
        project.SetSgCode(" sg0042 ");
        Assert.Equal("SG0042", project.SgCode);
        Assert.Equal(updated, project.UpdatedAt);
    }

    private static Project NewProject() => new(Guid.NewGuid(), "Project", "City", ProductLine.Classic, "google-test-subject");
}
