using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Tests.Application;
public sealed class InstallationTechnicalTests
{
    [Theory]
    [InlineData("HingedDoor", "Inward", "Left")]
    [InlineData("HingedDoor", "Outward", "Right")]
    [InlineData("HingedWindow", "Inward", null)]
    [InlineData("HingedWindow", "Outward", null)]
    [InlineData("NotApplicable", null, null)]
    [InlineData("Other", null, null)]
    public void ValidConditionalDetailsRemainIndependentOfFreeSystem(string type, string? direction, string? hand)
    {
        var at = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var checklist = new MeasurementChecklist(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), at, "installer");
        var row = MeasurementStructure.Added(checklist, "Original", 1, 2, null, at, "installer");
        Assert.Null(row.Technical); Assert.False(row.IsTechnicallyComplete);
        row.RecordActual(2.000m, 2.400m, "Completely free text", at, "installer");
        row.RecordSiteDetails("Agreed", "Room", null, at, "installer");
        var details = new InstallationTechnicalDetails("Confirmed", 1100, "Outside", type, direction, hand, true, 123.5m);
        row.RecordTechnical(details, at, "installer"); row.ResolveComparison("NotComparable", "No commercial reference", at, "installer"); Assert.True(row.IsTechnicallyComplete);
        Assert.Equal(1100, row.Technical!.HandleHeightMm); Assert.Equal(123.5m, row.Technical.AlfajiaMeasurementMm);
        row.RecordTechnical(details with { ViewSide = "Inside" }, at, "installer"); Assert.Equal(hand, row.Technical!.OpeningHand);
        row.RecordTechnical(details with { HandleStatus = "NotApplicable", HandleHeightMm = null, HasAlfajia = false, AlfajiaMeasurementMm = null }, at, "installer");
        Assert.Null(row.Technical!.HandleHeightMm); Assert.Null(row.Technical.AlfajiaMeasurementMm);
        Assert.Equal("Completely free text", row.ActualSystemText); Assert.Equal(1, row.Width); Assert.Equal(2, row.Height);
        Assert.Equal(2.000m, row.ActualWidth); Assert.Equal(2.400m, row.ActualHeight);
    }
    [Fact]
    public void BrowserIccVariantRejectsAnimationAlphaMetadataBadLengthsAndMismatchedDimensions()
    {
        var upload = BrowserSketchFixture.Upload(); MeasurementSketch.Validate(upload);
        foreach (var (offset, value) in new (int, byte)[] { (20, 0x22), (20, 0x30), (20, 0x28), (20, 0), (21, 1), (24, 0), (27, 0), (30, (byte)'X'), (34, 255), (38, 255), (74, 0), (494, (byte)'X'), (508, 0) })
        {
            var bytes = upload.Webp.ToArray(); bytes[offset] = value;
            Assert.Throws<SnG.Control.Application.Features.ProjectDocuments.DocumentException>(() => MeasurementSketch.Validate(upload with { Webp = bytes }));
        }
        var extra = upload.Webp.Concat(new byte[8]).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(4), (uint)extra.Length - 8);
        Assert.Throws<SnG.Control.Application.Features.ProjectDocuments.DocumentException>(() => MeasurementSketch.Validate(upload with { Webp = extra }));
        foreach (var size in new[] { 29, 30, 37, 100, 494, upload.Webp.Length - 1 })
        {
            var truncated = upload.Webp[..size];
            if (size >= 30) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4), (uint)size - 8);
            Assert.Throws<SnG.Control.Application.Features.ProjectDocuments.DocumentException>(() => MeasurementSketch.Validate(upload with { Webp = truncated }));
        }
        MeasurementSketch.Validate(SketchFixture.Upload());
    }
    [Fact]
    public void InvalidOrContradictoryValuesAreRejectedAndDraftsAreExplicit()
    {
        var d = new InstallationTechnicalDetails("Confirmed", 1100, "Inside", "HingedDoor", "Outward", "Left", true, 100);
        foreach (var invalid in new[] { d with { HandleHeightMm = 0 }, d with { HandleHeightMm = 2401 }, d with { HandleHeightMm = 5001 }, d with { HandleStatus = null }, d with { HandleStatus = "NotApplicable" }, d with { ViewSide = "External" }, d with { OpeningType = "Other" }, d with { OpeningType = "HingedWindow" }, d with { OpeningHand = "HandleSide" }, d with { HasAlfajia = false }, d with { AlfajiaMeasurementMm = -1 }, d with { AlfajiaMeasurementMm = 10001 } }) Assert.Throws<ArgumentException>(() => invalid.Validate(2.4m));
        var pending = d with { HandleHeightMm = null, OpeningDirection = null }; pending.Validate(2.4m); Assert.False(pending.IsFullyComplete);
        Assert.Throws<ArgumentException>(() => d.Validate(null));
    }
    [Fact]
    public void SketchLimitsRejectUntrustedContentAndAcceptOpaqueWebp()
    {
        var upload = SketchFixture.Upload(); MeasurementSketch.Validate(upload);
        var stroke = new SketchStroke("Pen", "#111111", 3, [new(.1, .2), new(.9, .8)]);
        MeasurementSketch.Validate(upload with { Drawing = upload.Drawing with { Strokes = [stroke] } });
        foreach (var invalid in new[] {
            upload with { Webp = [1, 2, 3] }, upload with { Webp = new byte[512001] },
            upload with { Drawing = upload.Drawing with { Width = 9999 } },
            upload with { Drawing = upload.Drawing with { Version = 2 } },
            upload with { Drawing = upload.Drawing with { Strokes = Enumerable.Repeat(stroke, 201).ToArray() } },
            upload with { Drawing = upload.Drawing with { Strokes = [stroke with { Color = "url(https://invalid.test)" }] } },
            upload with { Drawing = upload.Drawing with { Strokes = [stroke with { Points = [new(double.NaN, 0)] }] } },
            upload with { Drawing = upload.Drawing with { Strokes = [stroke with { Points = [new(1.1, 0)] }] } },
            upload with { Drawing = upload.Drawing with { Strokes = [stroke with { Points = Enumerable.Repeat(new SketchPoint(0, 0), 2001).ToArray() }] } },
            upload with { Drawing = upload.Drawing with { Strokes = Enumerable.Repeat(stroke with { Points = Enumerable.Repeat(new SketchPoint(0, 0), 2000).ToArray() }, 11).ToArray() } }
        }) Assert.Throws<DocumentException>(() => MeasurementSketch.Validate(invalid));
    }
}
