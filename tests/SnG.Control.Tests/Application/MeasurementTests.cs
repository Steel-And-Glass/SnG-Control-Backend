using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Application;

public sealed class MeasurementTests
{
    [Theory]
    [InlineData("Casa MDK", "Radicacion de medidas-Casa MDK.pdf")]
    [InlineData("Ático norte", "Radicacion de medidas-Ático norte.pdf")]
    [InlineData("../Casa\\Norte:*?<>|\"\r\n", "Radicacion de medidas-.._Casa_Norte_________.pdf")]
    public void ReportNameIsReadableDeterministicAndSafe(string project, string expected)
    {
        Assert.Equal(expected, MeasurementReportName.ForProject(project));
        Assert.Equal(MeasurementReportName.ForProject(project), MeasurementReportName.ForProject(project));
    }
    [Fact]
    public void ReportNamePreservesExtensionAtMaximumLength()
    {
        var name = MeasurementReportName.ForProject(new string('a', 500));
        Assert.Equal(200, name.Length); Assert.EndsWith(".pdf", name);
    }
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData(new[] { 1 }, 1)]
    [InlineData(new[] { 6 }, 6)]
    [InlineData(new[] { 1, 6, 2 }, 9)]
    [InlineData(new[] { 6, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, 21)]
    public void ExpandsPersistentIndependentUnits(int[] quantities, int expected)
    {
        var checklist = Checklist();
        var rows = quantities.SelectMany((quantity, index) => {
            var item = new CommercialQuotationItem(checklist.CommercialQuotationId, Guid.NewGuid(), (CommercialItemSection)1, index + 1,
                new CatalogItemValues((index + 1).ToString(), "Room", "Window", null, null, null, null, 1.2m, 2m, 2.4m, quantity), 10, 100);
            return Enumerable.Range(1, quantity).Select(unit => MeasurementStructure.Quoted(checklist, item, unit, Now, "installer"));
        }).ToArray();
        Assert.Equal(expected, rows.Length); Assert.Equal(expected, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => { Assert.Null(r.MeasuredAt); Assert.Null(r.RetiredAt); Assert.Equal(checklist.ProjectId, r.ProjectId); });
        rows[0].Measure(true, Now, "installer"); Assert.Equal(1, MeasurementSummary.From(rows).Measured);
        rows[0].Measure(false, Now, "installer"); Assert.Equal(0, MeasurementSummary.From(rows).Measured);
    }
    [Fact]
    public void AddedRetiredAndZeroActiveHaveCorrectProgress()
    {
        var checklist = Checklist(); var row = MeasurementStructure.Added(checklist, "Door", 1.01m, 2m, "Site note", Now, "installer");
        Assert.Equal("Site note", row.Note); Assert.Equal(1.01m, row.Width); Assert.Null(row.MeasuredAt);
        row.Measure(true, Now, "installer"); Assert.Equal("installer", row.MeasuredBySubject);
        row.Retire(Now, "installer"); Assert.Equal("installer", row.RetiredBySubject);
        Assert.Equal(new MeasurementSummary(0, 1, 1, 0, 0, 0, 0), MeasurementSummary.From([row]));
        Assert.Throws<InvalidOperationException>(() => row.Retire(Now, "installer"));
        Assert.Throws<InvalidOperationException>(() => row.Measure(false, Now, "installer"));
        Assert.Throws<ArgumentException>(() => MeasurementStructure.Added(checklist, "Door", 0, 1, null, Now, "installer"));
        checklist.Submit(Now, "installer", "{}"); Assert.Throws<InvalidOperationException>(() => checklist.Advance());
        Assert.Throws<InvalidOperationException>(() => MeasurementStructure.Added(checklist, "Door", 1, 1, null, Now, "installer"));
    }
    [Theory]
    [InlineData(SystemRole.CommercialAdvisor)] [InlineData(SystemRole.Accounting)] [InlineData(SystemRole.Management)] [InlineData(SystemRole.CommercialHead)] [InlineData(SystemRole.Engineering)]
    public async Task SameSubjectOtherActiveRoleCannotWrite(SystemRole role)
    {
        var service = new MeasurementService(new UnusedStore(), new IdentityDouble("installer"), new ActiveRoleDouble(role), new Installer());
        Assert.Equal(403, (await Assert.ThrowsAsync<DocumentException>(() => service.InitializeAsync(Guid.NewGuid(), default))).Status);
        await Assert.ThrowsAsync<DocumentException>(() => service.ExecuteAsync(Guid.NewGuid(), new(Guid.NewGuid(), 0, "Submit", DocumentIds: [Guid.NewGuid()]), default));
    }
    [Fact]
    public async Task WrongConfiguredSubjectAndMalformedCommandAreRejected()
    {
        var service = new MeasurementService(new UnusedStore(), new IdentityDouble("other"), new ActiveRoleDouble(SystemRole.Installation), new Installer());
        await Assert.ThrowsAsync<DocumentException>(() => service.InitializeAsync(Guid.NewGuid(), default));
        service = new(new UnusedStore(), new IdentityDouble("installer"), new ActiveRoleDouble(SystemRole.Installation), new Installer());
        foreach (var command in new[] { new MeasurementCommand(Guid.Empty, 0, "Measure"), new(Guid.NewGuid(), 0, "Add", Width: -1, Height: 1, Description: "Door"), new(Guid.NewGuid(), 0, "Submit", DocumentIds: []), new(Guid.NewGuid(), 0, "Retire", StructureId: Guid.NewGuid(), Measured: true) })
            Assert.Equal("InvalidMeasurementCommand", (await Assert.ThrowsAsync<DocumentException>(() => service.ExecuteAsync(Guid.NewGuid(), command, default))).Code);
    }
    [Fact]
    public void ActualMeasurementsPreserveOriginalsAndLegacySnapshotContract()
    {
        var checklist = Checklist(); var row = MeasurementStructure.Added(checklist, "Original door", 1, 2, null, Now, "installer");
        row.RecordActual(1.1234567m, 2.2345678m, "  Site system  ", Now, "installer");
        Assert.Equal(1.1234567m, row.ActualWidth); Assert.Equal(2.2345678m, row.ActualHeight); Assert.Equal("Site system", row.ActualSystemText);
        Assert.Equal(1, row.Width); Assert.Equal(2, row.Height); Assert.Equal("Original door", row.Description); Assert.Null(row.MeasuredAt);
        Assert.Throws<ArgumentException>(() => row.RecordActual(0, 2, "System", Now, "installer"));
        Assert.Throws<ArgumentException>(() => row.RecordActual(1, 2, " ", Now, "installer"));
        row.Retire(Now, "installer"); Assert.Throws<InvalidOperationException>(() => row.RecordActual(1, 2, "System", Now, "installer"));
        var oldJson = System.Text.Json.JsonSerializer.Serialize(new { row.Id, ItemId = (Guid?)null, UnitOrdinal = 1, UnitCount = 1, ItemNumber = (string?)null, row.Description, Location = (string?)null, row.Width, row.Height, Note = "Historical", CreatedAt = Now, MeasuredAt = (DateTime?)null, RetiredAt = (DateTime?)null });
        var old = System.Text.Json.JsonSerializer.Deserialize<MeasurementStructureView>(oldJson)!;
        Assert.Null(old.ActualWidth); Assert.Null(old.ActualHeight); Assert.Null(old.ActualSystemText); Assert.Equal("Historical", old.Note);
        var command = new MeasurementCommand(Guid.NewGuid(), 0, "Measure", row.Id, true);
        var oldCommand = new { command.RequestKey, command.ExpectedRevision, command.Action, command.StructureId, command.Measured, command.Description, command.Width, command.Height, command.Note, command.DocumentIds };
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(oldCommand), System.Text.Json.JsonSerializer.Serialize(command));
    }
    [Fact]
    public async Task ActualMeasurementsRejectIncompleteOrMixedCommandsAndNewNotes()
    {
        var service = new MeasurementService(new UnusedStore(), new IdentityDouble("installer"), new ActiveRoleDouble(SystemRole.Installation), new Installer());
        var valid = new MeasurementCommand(Guid.NewGuid(), 0, "RecordActual", Guid.NewGuid(), ActualWidth: 1, ActualHeight: 2, ActualSystemText: "System");
        foreach (var command in new[] { valid with { ActualWidth = null }, valid with { ActualHeight = -1 }, valid with { ActualSystemText = " " }, valid with { ActualSystemText = new string('x', 2001) }, valid with { Width = 9 }, valid with { Measured = true }, valid with { Action = "Retire" }, new(Guid.NewGuid(), 0, "Add", Description: "Door", Width: 1, Height: 2, Note: "Removed input") })
            Assert.Equal("InvalidMeasurementCommand", (await Assert.ThrowsAsync<DocumentException>(() => service.ExecuteAsync(Guid.NewGuid(), command, default))).Code);
    }
    [Theory]
    [InlineData("Agreed")][InlineData("Production")][InlineData("Opening")]
    public void SiteDetailsAndReinstatementPreserveOriginalsAndRequireNewMeasurement(string type)
    {
        var row = MeasurementStructure.Added(Checklist(), "Original", 2, 2.4m, "Historical", Now, "installer");
        row.RecordActual(2, 2.4m, "Site", Now, "installer");
        row.RecordSiteDetails(type, "  Room B  ", "  Site observations  ", Now, "installer");
        row.RecordTechnical(new("NotApplicable", null, "Outside", "Other", null, null, false, null), Now, "installer");
        var sketch = Guid.NewGuid(); row.SetSketch(sketch, Now, "installer");
        row.ResolveComparison("NotComparable", "Onsite addition", Now, "installer");
        Assert.True(row.IsTechnicallyComplete); row.Measure(true, Now, "installer"); row.Retire(Now, "installer");
        var id = row.Id; row.Reinstate(Now.AddSeconds(1), "installer");
        Assert.Equal(id, row.Id); Assert.Null(row.RetiredAt); Assert.Null(row.MeasuredAt); Assert.Null(row.MeasuredBySubject);
        Assert.Equal(sketch, row.SketchVersionId); Assert.Equal("Room B", row.ActualLocation); Assert.Equal(type, row.MeasurementType);
        Assert.Equal("Site observations", row.InstallationObservations); Assert.Equal("Historical", row.Note); Assert.Equal("Original", row.Description);
        Assert.Throws<InvalidOperationException>(() => row.Reinstate(Now.AddSeconds(2), "installer"));
        row.RecordSiteDetails(null, " ", " ", Now.AddSeconds(2), "installer");
        Assert.False(row.IsTechnicallyComplete); Assert.Null(row.ActualLocation); Assert.Null(row.InstallationObservations);
        Assert.Throws<ArgumentException>(() => row.RecordSiteDetails("Invented", "Room", null, Now, "installer"));
        Assert.Throws<ArgumentException>(() => row.RecordSiteDetails(type, new string('x', 501), null, Now, "installer"));
        Assert.Throws<ArgumentException>(() => row.RecordSiteDetails(type, "Room", new string('x', 4001), Now, "installer"));
    }
    private static MeasurementChecklist Checklist() => new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Now, "installer");
    private sealed class Installer : IInstallationAssigneeIdentity { public string Subject => "installer"; }
    private sealed class UnusedStore : IMeasurementStore
    {
        public Task<SketchRead?> ReadSketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token) => Task.FromResult<SketchRead?>(null);
        public Task<byte[]?> ReadSketchImageAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token) => Task.FromResult<byte[]?>(null);
        public Task<MeasurementState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token) => throw new NotImplementedException();
        public Task InitializeAsync(Guid id, DecisionActor actor, CancellationToken token) => throw new NotImplementedException();
        public Task ExecuteAsync(Guid id, MeasurementCommand command, DecisionActor actor, CancellationToken token) => throw new NotImplementedException();
    }
}
