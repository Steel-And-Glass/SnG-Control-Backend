using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Application;
public sealed class InstallationAssessmentTests
{
    static readonly DateTime At = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    static MeasurementStructure Row(decimal width, decimal height)
    {
        var c = new MeasurementChecklist(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), At, "installer");
        var item = new CommercialQuotationItem(c.CommercialQuotationId, Guid.NewGuid(), CommercialItemSection.Main, 5, new("V1", "Room", "System", null, null, null, null, 2, 2.4m, 4.8m, 1), 1, 1);
        var r = MeasurementStructure.Quoted(c, item, 1, At, "installer");
        r.RecordActual(width, height, "Site", At, "installer"); r.RecordSiteDetails("Agreed", "Room", null, At, "installer");
        r.RecordTechnical(new("NotApplicable", null, "Outside", "Other", null, null, false, null), At, "installer"); return r;
    }
    [Theory]
    [InlineData(99, 0, false)][InlineData(100, 0, false)][InlineData(101, 0, true)]
    [InlineData(-101, 0, true)][InlineData(0, 101, true)][InlineData(0, -101, true)]
    public void ExplicitEquivalenceUsesAbsoluteStrictThreshold(int widthMm, int heightMm, bool exceeds)
    {
        var r = Row(2 + widthMm / 1000m, 2.4m + heightMm / 1000m);
        Assert.Equal("PendingReview", r.ComparisonStatus); Assert.Null(r.WidthDifferenceMm); Assert.False(r.IsTechnicallyComplete);
        r.ResolveComparison("Comparable", "Reference verified", At, "installer");
        Assert.Equal(Math.Abs(widthMm), r.WidthDifferenceMm); Assert.Equal(Math.Abs(heightMm), r.HeightDifferenceMm);
        Assert.Equal(exceeds ? "OverThreshold" : "NoDimensionalChange", r.ComparisonStatus);
        Assert.Equal(!exceeds, r.IsTechnicallyComplete); Assert.Equal(2, r.Width); Assert.Equal(2.4m, r.Height);
        r.RecordSiteDetails("Opening", "Room", null, At, "installer");
        Assert.False(r.ComparisonCurrent); Assert.Equal("PendingReview", r.ComparisonStatus);
    }
    [Fact]
    public void ModificationNeedsExplicitSketchBindingAndFreshEvidence()
    {
        var r = Row(2, 2.4m); r.ResolveComparison("NotComparable", "Commercial reference unclassified", At, "installer");
        Assert.Equal("PendingReview", r.ComparisonStatus); Assert.True(r.IsTechnicallyComplete);
        var sketch = Guid.NewGuid(); r.SetSketch(sketch, At, "installer");
        r.RecordModification("System", null, null, null, At, "installer"); var draftId = r.Modification!.VersionId;
        Assert.False(r.IsTechnicallyComplete);
        r.RecordModification("System", "Change", "Detailed configuration", sketch, At, "installer");
        Assert.NotEqual(draftId, r.Modification!.VersionId); Assert.True(r.IsTechnicallyComplete);
        Assert.Throws<ArgumentException>(() => r.RecordModification("System", "Reason", "Description", Guid.NewGuid(), At, "installer"));
        r.RecordActual(2.01m, 2.4m, "Site", At, "installer"); Assert.False(r.ModificationComplete); Assert.False(r.ComparisonCurrent);
        r.ResolveComparison("Comparable", "Checked", At, "installer"); Assert.False(r.IsTechnicallyComplete);
        r.RecordModification("System", "Change", "Updated configuration", sketch, At, "installer"); Assert.True(r.IsTechnicallyComplete);
    }
    [Fact]
    public void SketchV2ValidatesEditableTextWithoutBreakingV1()
    {
        var v1 = SketchFixture.Upload(); MeasurementSketch.Validate(v1);
        var a = new SketchAnnotation(Guid.NewGuid(), "Width 2000 mm", .2, .3, 24, "#111111", 0);
        var v2 = v1 with { Drawing = v1.Drawing with { Version = 2, Annotations = [a] } }; MeasurementSketch.Validate(v2);
        foreach (var bad in new[] { a with { Content = "" }, a with { Content = new string('x', 301) }, a with { X = double.NaN }, a with { Y = 2 }, a with { Size = 100 }, a with { Color = "red" }, a with { Id = Guid.Empty } })
            Assert.Throws<DocumentException>(() => MeasurementSketch.Validate(v2 with { Drawing = v2.Drawing with { Annotations = [bad] } }));
        Assert.Throws<DocumentException>(() => MeasurementSketch.Validate(v2 with { Drawing = v2.Drawing with { Annotations = [a, a] } }));
    }
}
