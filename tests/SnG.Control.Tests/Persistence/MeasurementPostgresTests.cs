using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;

public sealed class MeasurementPostgresTests
{
    [VerificationPostgresFact]
    public async Task PersistentChecklistConcurrentInitializationChangesSubmissionAndCompensation()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        SnGControlDbContext Db(bool fail = false) => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).AddInterceptors(new FailSave(fail)).Options);
        using var files = new TemporaryStorage(); var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = files.Root }));
        var clock = new ControlledTimeProvider(TestTiming.Value.UtcNow); var timing = new SnG.Control.Application.Scheduling.TaskTiming(clock, new());
        EfMeasurementStore Store(SnGControlDbContext db) => new(db, timing, new(db, new ProjectNotificationPlanner(timing)), storage, new IdentityDouble("engineer"));
        EfProjectDocumentStore Documents(SnGControlDbContext db, string subject = "installer", SystemRole role = SystemRole.Installation) => new(db, storage, new(new IdentityDouble(subject), new ActiveRoleDouble(role), new IdentityDouble("budgets")), TestTiming.Value);
        var actor = new DecisionActor("installer", "installer@example.test", "Installer"); var now = clock.Now;
        await using var db = Db(); await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        var client = new Client("Measurement synthetic"); var project = new Project(client.Id, "Measurement synthetic", "City", ProductLine.Classic, "advisor");
        db.AddRange(client, project); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Error("MeasurementUnavailable", () => Store(db).InitializeAsync(project.Id, actor, default));
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
        var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1)); present.Start(now.AddDays(-1), now.AddDays(1)); present.Complete(now);
        var decision = new ClientQuotationDecision(project.Id, quote.Id, present.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null);
        var accounting = new ProjectTask(project.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now); accounting.StartUntracked(now); accounting.Complete(now);
        var handoff = new AccountingHandoff(project.Id, decision.Id, quote.Id, 1, accounting.Id, now, "advisor", null, null);
        var task = new ProjectTask(project.Id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, "installer", now);
        db.AddRange(version, catalog, quote, present, decision, accounting, handoff, new InitialDeposit(handoff, 50, now, now, null, null, "accountant", null, null), task);
        foreach (var (quantity, index) in new[] { 6, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }.Select((q, i) => (q, i)))
        {
            var values = new CatalogItemValues((index + 1).ToString(), "Room", "Window", null, null, null, null, 1.2m, 2, 2.4m, quantity);
            var source = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, index + 1, values);
            db.Add(new CommercialCatalogItemExtraction(source.Id, values, CatalogItemImageStatus.Stored, $"projects/{project.Id:D}/budget-quotations/{version.Id:D}/images/{source.Id:D}.png", "image/png"));
            db.AddRange(source, new CommercialQuotationItem(quote.Id, source.Id, CommercialItemSection.Main, index + 1, values, 10, 100));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Error("MeasurementAccessDenied", () => Store(db).InitializeAsync(project.Id, actor with { Subject = "other" }, default));
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).InitializeAsync(project.Id, actor, default));
        Assert.False(await db.MeasurementChecklists.AnyAsync(c => c.ProjectId == project.Id));
        await using (var a = Db()) await using (var b = Db()) await Task.WhenAll(Store(a).InitializeAsync(project.Id, actor, default), Store(b).InitializeAsync(project.Id, actor, default));
        var state = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        Assert.Equal(21, state.Summary.Initial); Assert.Equal(21, state.Structures.Count); Assert.Equal(21, state.Summary.Pending);
        Assert.True(state.CanModify); Assert.Equal("InProgress", state.TaskStatus);
        Assert.Null(await Store(db).ReadAsync(project.Id, new("other", SystemRole.Installation), default));
        Assert.Null(await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Accounting), default));
        Assert.False((await Store(db).ReadAsync(project.Id, new("manager", SystemRole.Management), default))!.CanModify);
        async Task Refresh()
        {
            await using var reopened = Db();
            state = (await Store(reopened).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
        }
        async Task Execute(MeasurementCommand command) { await using var c = Db(); await Store(c).ExecuteAsync(project.Id, command, actor, default); await Refresh(); }
        MeasurementCommand Command(string action, Guid? id = null, bool? measured = null) => new(Guid.NewGuid(), state.Revision, action, id, measured);
        var quotationBefore = System.Text.Json.JsonSerializer.Serialize(await db.CommercialQuotationItems.AsNoTracking().Where(i => i.CommercialQuotationId == quote.Id).OrderBy(i => i.Id).ToArrayAsync());
        var first = state.Structures.First(s => s.UnitCount == 6);
        Assert.All(state.Structures, s => { Assert.Null(s.ActualWidth); Assert.Null(s.ActualHeight); Assert.Null(s.ActualSystemText); Assert.NotNull(s.ImageCatalogItemId); });
        var sourceItem = await db.CommercialQuotationItems.AsNoTracking().SingleAsync(i => i.Id == first.ItemId);
        Assert.Equal(sourceItem.CommercialCatalogItemId, first.ImageCatalogItemId);
        Assert.NotNull(await new EfQuotationReadStore(db).GetBudgetImageAsync(project.Id, first.ImageCatalogItemId!.Value, default));
        Assert.Null(await new EfQuotationReadStore(db).GetBudgetImageAsync(Guid.NewGuid(), first.ImageCatalogItemId.Value, default));
        await using (var invalid = await db.Database.BeginTransactionAsync())
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MeasurementStructures\" SET \"ActualWidth\" = 0 WHERE \"Id\" = {first.Id}"));
            Assert.Equal("23514", violation.SqlState); Assert.Equal("CK_MeasurementStructures_Actual", violation.ConstraintName);
            await invalid.RollbackAsync();
        }
        await Error("MeasurementsIncomplete", () => Store(db).ExecuteAsync(project.Id, Command("Measure", first.Id, true), actor, default));
        var capture = new MeasurementCommand(Guid.NewGuid(), state.Revision, "RecordActual", first.Id, ActualWidth: 1.2345m, ActualHeight: 2.3456m, ActualSystemText: "  Site custom system  ", MeasurementType: "Agreed", ActualLocation: "Room");
        await Error("MeasurementAccessDenied", () => Store(db).ExecuteAsync(project.Id, capture, actor with { Subject = "other" }, default));
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).ExecuteAsync(project.Id, capture, actor, default));
        await Refresh(); Assert.Null(state.Structures.Single(s => s.Id == first.Id).ActualWidth);
        await Execute(capture); await Execute(capture);
        var recorded = state.Structures.Single(s => s.Id == first.Id);
        Assert.Equal(1.2345m, recorded.ActualWidth); Assert.Equal(2.3456m, recorded.ActualHeight); Assert.Equal("Site custom system", recorded.ActualSystemText);
        Assert.Equal(first.Width, recorded.Width); Assert.Equal(first.Height, recorded.Height); Assert.Equal(first.Description, recorded.Description);
        Assert.Equal(21, state.Summary.Pending); Assert.Equal(0, state.Summary.Measured);
        var auditCapture = Assert.Single(state.History, e => e.Action == "RecordActual");
        Assert.Null(auditCapture.Details!.Before.Width); Assert.Equal(1.2345m, auditCapture.Details.After.Width); Assert.Equal("Installer", auditCapture.Actor.Name);
        await Error("MeasurementRevisionConflict", () => Store(db).ExecuteAsync(project.Id, capture with { RequestKey = Guid.NewGuid(), ActualWidth = 9 }, actor, default));
        await Execute(new(Guid.NewGuid(), state.Revision, "RecordTechnical", first.Id, Technical: CompleteTechnical()));
        Assert.Equal(CompleteTechnical(), state.Structures.Single(s => s.Id == first.Id).Technical);
        Assert.Null(state.Structures.First(s => s.Id != first.Id).Technical);
        await Error("MeasurementStructureNotFound", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "RecordTechnical", Guid.NewGuid(), Technical: CompleteTechnical()), actor, default));
        var sketchCommand = new MeasurementCommand(Guid.NewGuid(), state.Revision, "RecordSketch", first.Id, Sketch: SketchFixture.Upload());
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).ExecuteAsync(project.Id, sketchCommand, actor, default));
        Assert.Empty(Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories));
        await using (var a = Db()) await using (var b = Db()) await Task.WhenAll(Store(a).ExecuteAsync(project.Id, sketchCommand, actor, default), Store(b).ExecuteAsync(project.Id, sketchCommand, actor, default));
        await Refresh(); await Execute(sketchCommand);
        var firstVersion = state.Structures.Single(s => s.Id == first.Id).SketchVersionId!.Value;
        Assert.Null(state.Structures.First(s => s.Id != first.Id).SketchVersionId);
        Assert.Single(await db.MeasurementSketchVersions.Where(v => v.ProjectId == project.Id).ToArrayAsync());
        Assert.Equal(sketchCommand.Sketch!.Webp, await Store(db).ReadSketchImageAsync(project.Id, first.Id, firstVersion, new("manager", SystemRole.Management), default));
        Assert.Null(await Store(db).ReadSketchAsync(project.Id, first.Id, firstVersion, new("other", SystemRole.Installation), default));
        Assert.Null(await Store(db).ReadSketchAsync(Guid.NewGuid(), first.Id, firstVersion, new("manager", SystemRole.Management), default));
        Assert.Null(await Store(db).ReadSketchAsync(project.Id, Guid.NewGuid(), firstVersion, new("manager", SystemRole.Management), default));
        await Error("MeasurementSketchRateLimit", () => Store(db).ExecuteAsync(project.Id, sketchCommand with { RequestKey = Guid.NewGuid(), ExpectedRevision = state.Revision }, actor, default));
        clock.Now = clock.Now.AddSeconds(2);
        await Execute(sketchCommand with { RequestKey = Guid.NewGuid(), ExpectedRevision = state.Revision, Sketch = BrowserSketchFixture.Upload() });
        var finalVersion = state.Structures.Single(s => s.Id == first.Id).SketchVersionId!.Value;
        Assert.Equal(BrowserSketchFixture.Upload().Webp, await Store(db).ReadSketchImageAsync(project.Id, first.Id, finalVersion, new("manager", SystemRole.Management), default));
        var savedSketch = await Store(db).ReadSketchAsync(project.Id, first.Id, finalVersion, new("installer", SystemRole.Installation), default);
        Assert.Equal("2000 mm · prueba de croquis", Assert.Single(savedSketch!.Drawing.Annotations!).Content);
        var sibling = state.Structures.First(s => s.ItemId == first.ItemId && s.Id != first.Id);
        await Execute(new(Guid.NewGuid(), state.Revision, "RecordSketch", sibling.Id, Sketch: SketchFixture.Upload()));
        Assert.NotEqual(finalVersion, state.Structures.Single(s => s.Id == sibling.Id).SketchVersionId);
        Assert.Null(await Store(db).ReadSketchAsync(project.Id, sibling.Id, finalVersion, new("manager", SystemRole.Management), default));
        Assert.NotEqual(firstVersion, finalVersion);
        Assert.NotNull(await Store(db).ReadSketchAsync(project.Id, first.Id, firstVersion, new("manager", SystemRole.Management), default));
        Assert.Equal(firstVersion, state.History.Last(e => e.Action == "RecordSketch" && e.StructureId == first.Id).Sketch!.Before);
        await Execute(new(Guid.NewGuid(), state.Revision, "ResolveComparison", first.Id, Comparison: new("NotComparable", "Synthetic reference has no dimensional classification")));
        var mark = Command("Measure", first.Id, true);
        await using (var a = Db()) await using (var b = Db()) await Task.WhenAll(Store(a).ExecuteAsync(project.Id, mark, actor, default), Store(b).ExecuteAsync(project.Id, mark, actor, default));
        await Refresh(); Assert.NotNull(state.Structures.Single(s => s.Id == first.Id).MeasuredAt); Assert.Equal(1, state.Summary.Measured); Assert.Single(state.History, h => h.Action == "Measure");
        await Error("MeasurementRequestConflict", () => Store(db).ExecuteAsync(project.Id, mark with { Measured = false }, actor, default));
        await Error("MeasurementRevisionConflict", () => Store(db).ExecuteAsync(project.Id, mark with { RequestKey = Guid.NewGuid() }, actor, default));
        await Execute(Command("Measure", first.Id, false)); Assert.Equal(0, state.Summary.Measured); Assert.Null(state.Structures.Single(s => s.Id == first.Id).MeasuredAt); Assert.Contains(state.History, h => h.Action == "Unmeasure" && h.Actor.Name == "Installer");
        await Execute(new(Guid.NewGuid(), state.Revision, "Add", Description: "Onsite door", Width: 1.3m, Height: 2.1m));
        var added = state.Structures.Single(s => s.ItemId is null); Assert.Null(added.MeasuredAt); Assert.Equal(22, state.Summary.Active);
        Assert.Equal(1.3m, added.ActualWidth); Assert.Equal(2.1m, added.ActualHeight); Assert.Equal("Onsite door", added.ActualSystemText); Assert.Null(added.ImageCatalogItemId);
        await Execute(new(Guid.NewGuid(), state.Revision, "RecordActual", added.Id, ActualWidth: 1.4m, ActualHeight: 2.2m, ActualSystemText: "Door revised onsite", MeasurementType: "Agreed", ActualLocation: "Room"));
        var revisedAdded = state.Structures.Single(s => s.Id == added.Id); Assert.Equal(1.4m, revisedAdded.ActualWidth); Assert.Equal(1.3m, revisedAdded.Width);
        Assert.Equal(1.3m, state.History.Last(e => e.Action == "RecordActual").Details!.Before.Width);
        await Execute(new(Guid.NewGuid(), state.Revision, "RecordTechnical", added.Id, Technical: CompleteTechnical()));
        await Execute(new(Guid.NewGuid(), state.Revision, "RecordSketch", added.Id, Sketch: SketchFixture.Upload()));
        await Execute(Command("Retire", added.Id));
        await Error("MeasurementStructureRetired", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "RecordTechnical", added.Id, Technical: CompleteTechnical()), actor, default));
        await Error("MeasurementStructureRetired", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), state.Revision, "RecordSketch", added.Id, Sketch: SketchFixture.Upload()), actor, default));
        var retire = Command("Retire", first.Id);
        await using (var a = Db()) await using (var b = Db())
        {
            async Task<string> Retire(SnGControlDbContext context, MeasurementCommand command)
            { try { await Store(context).ExecuteAsync(project.Id, command, actor, default); return "OK"; } catch (DocumentException e) { return e.Code; } }
            var outcomes = await Task.WhenAll(Retire(a, retire), Retire(b, retire with { RequestKey = Guid.NewGuid() }));
            Assert.Single(outcomes, o => o == "OK"); Assert.Single(outcomes, o => o == "MeasurementRevisionConflict");
        }
        await Refresh(); Assert.Equal(20, state.Summary.Active); Assert.Equal(20, state.Summary.Pending); Assert.Equal(0, state.Summary.Progress); Assert.Equal(2, state.Summary.Retired);
        Assert.Equal(22, state.Structures.Count); Assert.Equal(5, state.Structures.Count(s => s.ItemId == first.ItemId && s.RetiredAt is null));
        foreach (var retiredId in new[] { first.Id, added.Id })
        {
            Assert.NotNull(state.Structures.Single(s => s.Id == retiredId).RetiredAt);
            var audit = Assert.Single(state.History, h => h.Action == "Retire" && h.StructureId == retiredId);
            Assert.Equal("Installer", audit.Actor.Name); Assert.Equal(DateTimeKind.Utc, audit.At.Kind);
            await using var reopened = Db();
            var saved = await reopened.MeasurementStructures.AsNoTracking().SingleAsync(s => s.Id == retiredId);
            Assert.Equal("installer", saved.RetiredBySubject); Assert.Equal(audit.At, saved.RetiredAt);
        }
        await Error("MeasurementStructureRetired", () => Store(db).ExecuteAsync(project.Id, Command("Retire", added.Id), actor, default));
        var orderBeforeReinstate = state.Structures.Select(s => s.Id).ToArray();
        var beforeReinstate = state.Structures.Single(s => s.Id == first.Id);
        var reinstate = Command("Reinstate", first.Id);
        await Execute(reinstate); await Execute(reinstate);
        var reinstated = state.Structures.Single(s => s.Id == first.Id);
        Assert.Null(reinstated.RetiredAt); Assert.Null(reinstated.MeasuredAt);
        Assert.Equal(beforeReinstate with { RetiredAt = null, MeasuredAt = null }, reinstated);
        Assert.Equal(orderBeforeReinstate, state.Structures.Select(s => s.Id));
        Assert.Single(state.History, e => e.Action == "Reinstate" && e.StructureId == first.Id);
        Assert.Contains(state.History, e => e.Action == "Retire" && e.StructureId == first.Id);
        await Execute(Command("Retire", first.Id));
        await Error("MeasurementsIncomplete", () => Store(db).ExecuteAsync(project.Id, Command("Submit") with { DocumentIds = [Guid.NewGuid()] }, actor, default));
        foreach (var row in state.Structures.Where(s => s.RetiredAt is null).ToArray())
        {
            if (row.ActualWidth == null) await Execute(new(Guid.NewGuid(), state.Revision, "RecordActual", row.Id, ActualWidth: 2, ActualHeight: 2.4m, ActualSystemText: "Site", MeasurementType: "Agreed", ActualLocation: "Room"));
            await Execute(new(Guid.NewGuid(), state.Revision, "RecordTechnical", row.Id, Technical: CompleteTechnical()));
            await Execute(new(Guid.NewGuid(), state.Revision, "ResolveComparison", row.Id, Comparison: new("NotComparable", "Synthetic reference unclassified")));
            await Execute(Command("Measure", row.Id, true));
        }
        Assert.Equal(100, state.Summary.Progress); Assert.Equal(0, state.Summary.Pending);
        await Error("MeasurementAccessDenied", () => Documents(db, role: SystemRole.Management).UploadMeasurementsAsync(project.Id, Uploads("no.pdf"), default));
        await Error("MeasurementAccessDenied", () => Documents(db, "other").UploadMeasurementsAsync(project.Id, Uploads("no.pdf"), default));
        await Error("InvalidDocumentBatch", () => Documents(db).UploadMeasurementsAsync(project.Id, Uploads("../bad.pdf"), default));
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Documents(failed).UploadMeasurementsAsync(project.Id, Uploads("rollback.pdf"), default));
        Assert.Equal(4, Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).Length);
        var uploadKey = Guid.NewGuid();
        var upload = await Documents(db).UploadMeasurementsAsync(project.Id, Uploads("measures.pdf", "photo.jpg"), default, uploadKey); await Refresh();
        var retryUpload = await Documents(db).UploadMeasurementsAsync(project.Id, Uploads("measures.pdf", "photo.jpg"), default, uploadKey);
        Assert.Equal(upload.Documents.Select(d => d.Id).Order(), retryUpload.Documents.Select(d => d.Id).Order());
        Assert.Equal(2, state.Documents.Count); var ids = upload.Documents.Select(d => d.Id).ToArray();
        await Error("InvalidReplacement", () => Documents(db, "advisor", SystemRole.CommercialAdvisor).UploadAsync(project.Id, Replacing(ids[0]), default));
        var submit = Command("Submit") with { DocumentIds = ids };
        var imageExtraction = await db.CommercialCatalogItemExtractions.AsNoTracking().SingleAsync(e => e.CatalogItemId == first.ImageCatalogItemId);
        using var imageBitmap = new SkiaSharp.SKBitmap(213, 417);
        using (var canvas = new SkiaSharp.SKCanvas(imageBitmap)) canvas.Clear(SkiaSharp.SKColors.DarkBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(imageBitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var commercialBytes = encoded.ToArray();
        await storage.SaveAsync(imageExtraction.ImageStorageKey!, commercialBytes, default);
        // A storage key from another project is rejected even if the extraction relation is valid.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialCatalogItemExtractions\" SET \"ImageStorageKey\" = {"projects/" + Guid.NewGuid() + "/foreign.png"} WHERE \"CatalogItemId\" = {imageExtraction.CatalogItemId}");
        try { await Error("MeasurementReportUnavailable", () => Store(db).ExecuteAsync(project.Id, submit, actor, default)); }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialCatalogItemExtractions\" SET \"ImageStorageKey\" = {imageExtraction.ImageStorageKey} WHERE \"CatalogItemId\" = {imageExtraction.CatalogItemId}"); }
        await Refresh(); Assert.Null(state.SubmissionId);
        // Corrupt commercial bytes fail before closing the task.
        var commercialPath = Path.Combine(files.Root, imageExtraction.ImageStorageKey!.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(commercialPath, [1, 2, 3]);
        try { await Error("MeasurementReportUnavailable", () => Store(db).ExecuteAsync(project.Id, submit, actor, default)); }
        finally { await File.WriteAllBytesAsync(commercialPath, commercialBytes); }

        await Error("MeasurementDocumentsChanged", () => Store(db).ExecuteAsync(project.Id, submit with { DocumentIds = [ids[0]] }, actor, default));
        var sketchRecord = await db.MeasurementSketchVersions.AsNoTracking().SingleAsync(v => v.Id == finalVersion);
        var sketchPath = Path.Combine(files.Root, sketchRecord.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        var originalWebp = await File.ReadAllBytesAsync(sketchPath);
        await File.WriteAllBytesAsync(sketchPath, [1, 2, 3]);
        try { await Error("MeasurementSketchIntegrity", () => Store(db).ExecuteAsync(project.Id, submit, actor, default)); }
        finally { await File.WriteAllBytesAsync(sketchPath, originalWebp); }
        await Refresh(); Assert.Null(state.SubmissionId); Assert.False(await db.MeasurementReports.AnyAsync(r => r.ProjectId == project.Id));
        await Error("EngineeringAssigneeNotConfigured", () => new EfMeasurementStore(db, timing, new(db, new ProjectNotificationPlanner(timing)), storage).ExecuteAsync(project.Id, submit, actor, default));
        Assert.False(await db.EngineeringChecklists.AnyAsync(c => c.ProjectId == project.Id));
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).ExecuteAsync(project.Id, submit, actor, default));
        await Refresh(); Assert.Null(state.SubmissionId); Assert.False(await db.MeasurementReports.AnyAsync(r => r.ProjectId == project.Id)); Assert.Equal("InProgress", state.TaskStatus); Assert.Empty(await db.MeasurementSubmissionDocuments.Where(d => d.ProjectId == project.Id).ToArrayAsync());
        await using (var a = Db()) await using (var b = Db()) await Task.WhenAll(Store(a).ExecuteAsync(project.Id, submit, actor, default), Store(b).ExecuteAsync(project.Id, submit, actor, default));
        await Refresh(); Assert.NotNull(state.SubmissionId); Assert.Equal("Completed", state.TaskStatus); Assert.False(state.CanModify);
        Assert.Single(state.History, h => h.Action == "Submit"); Assert.Equal(22, state.Snapshot!.Structures.Count);
        var report = await Store(db).ReadReportAsync(project.Id, state.SubmissionId!.Value, new("installer", SystemRole.Installation), default);
        Assert.NotNull(report);
        Assert.Equal(finalVersion, state.Snapshot!.Structures.Single(s => s.Id == first.Id).SketchVersionId);
        Assert.Contains("/Width 213", System.Text.Encoding.Latin1.GetString(report.Bytes));
        Assert.Contains("/Height 417", System.Text.Encoding.Latin1.GetString(report.Bytes));
        Assert.Equal(commercialBytes, await storage.ReadAsync(imageExtraction.ImageStorageKey!, default));
        // The persisted PDF no longer depends on the commercial file.
        await storage.DeleteAsync(imageExtraction.ImageStorageKey!, default);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(report.Bytes, 0, 4));
        Assert.Equal("Radicacion de medidas-Measurement synthetic.pdf", report.FileName);
        Assert.Equal(report.Bytes, (await Store(db).ReadReportAsync(project.Id, state.SubmissionId.Value, new("manager", SystemRole.Management), default))!.Bytes);
        Assert.Null(await Store(db).ReadReportAsync(Guid.NewGuid(), state.SubmissionId.Value, new("manager", SystemRole.Management), default));
        Assert.Null(await Store(db).ReadReportAsync(project.Id, state.SubmissionId.Value, new("other", SystemRole.Installation), default));
        Assert.Null(await Store(db).ReadReportAsync(project.Id, state.SubmissionId.Value, new("installer", SystemRole.Accounting), default));
        Assert.Single(await db.MeasurementReports.Where(r => r.ProjectId == project.Id).ToArrayAsync());
        var savedReport = await db.MeasurementReports.AsNoTracking().SingleAsync(r => r.ProjectId == project.Id);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(report.Bytes)), savedReport.PdfSha256);
        if (Environment.GetEnvironmentVariable("SNG_SYNTHETIC_PDF_OUTPUT") is { } pdfPath) await File.WriteAllBytesAsync(pdfPath, report.Bytes);
        await using (var corruption = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MeasurementReports\" SET \"PdfSha256\" = {'0'.ToString()} WHERE \"ProjectId\" = {project.Id}");
            await Error("MeasurementReportIntegrity", () => Store(db).ReadReportAsync(project.Id, state.SubmissionId.Value, new("manager", SystemRole.Management), default));
            await corruption.RollbackAsync();
        }
        var frozen = state.Snapshot.Structures.Single(s => s.Id == first.Id);
        Assert.Equal(CompleteTechnical(), frozen.Technical); Assert.Equal(finalVersion, frozen.SketchVersionId);
        Assert.Equal(BrowserSketchFixture.Upload().Webp, await Store(db).ReadSketchImageAsync(project.Id, first.Id, finalVersion, new("manager", SystemRole.Management), default));
        await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, sketchCommand with { RequestKey = Guid.NewGuid(), ExpectedRevision = state.Revision }, actor, default));
        Assert.Equal(1.2345m, frozen.ActualWidth); Assert.Equal(2.3456m, frozen.ActualHeight); Assert.Equal("Site custom system", frozen.ActualSystemText); Assert.Equal(first.ImageCatalogItemId, frozen.ImageCatalogItemId);
        Assert.Equal(1.4m, state.Snapshot.Structures.Single(s => s.Id == added.Id).ActualWidth); Assert.Equal(ids.Order(), state.Snapshot.DocumentIds.Order());
        Assert.Equal(2, await db.MeasurementSubmissionDocuments.CountAsync(d => d.ProjectId == project.Id));
        await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, Command("Measure", first.Id, false), actor, default));
        await Error("MeasurementsLocked", () => Documents(db).UploadMeasurementsAsync(project.Id, Uploads("late.pdf"), default));
        await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, capture with { RequestKey = Guid.NewGuid(), ExpectedRevision = state.Revision }, actor, default));
        await Error("MeasurementsLocked", () => Store(db).ExecuteAsync(project.Id, Command("Retire", state.Structures.First(s => s.RetiredAt is null).Id), actor, default));
        Assert.Equal(quotationBefore, System.Text.Json.JsonSerializer.Serialize(await db.CommercialQuotationItems.AsNoTracking().Where(i => i.CommercialQuotationId == quote.Id).OrderBy(i => i.Id).ToArrayAsync()));
        Assert.Equal(14, await db.CommercialQuotationItems.CountAsync(i => i.CommercialQuotationId == quote.Id)); Assert.Equal(21, await db.CommercialQuotationItems.Where(i => i.CommercialQuotationId == quote.Id).SumAsync(i => i.Quantity));
        Assert.Equal(4, await db.ProjectTasks.CountAsync(t => t.ProjectId == project.Id));
        Assert.Null((await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).DeadlineAt);
        Assert.All(await db.MeasurementStructures.AsNoTracking().Where(s => s.ProjectId == project.Id && s.MeasuredAt != null).ToArrayAsync(), s => Assert.Equal("installer", s.MeasuredBySubject));
        Assert.Equal(6, Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).Length);
        await EngineeringPostgresChecks.VerifyAsync(() => Db(), project.Id, storage, timing, state);

    }
    [VerificationPostgresFact]
    public async Task OrderingUsesOriginalQuotationRowsForReadAndSnapshotWithoutChangingStoredData()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        SnGControlDbContext Db() => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).Options);
        using var files = new TemporaryStorage(); var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = files.Root }));
        EfMeasurementStore Store(SnGControlDbContext context) => new(context, TestTiming.Value, new(context, new ProjectNotificationPlanner(TestTiming.Value)), storage, new IdentityDouble("engineer"));
        var actor = new DecisionActor("installer", null, "Installer"); var now = TestTiming.Value.UtcNow;
        foreach (var count in new[] { 1, 4, 9 })
        {
            await using var db = Db(); await db.Database.MigrateAsync();
            var client = new Client("Measurement synthetic"); var project = new Project(client.Id, "Measurement synthetic", "City", ProductLine.Classic, "advisor");
            db.AddRange(client, project); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
            var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
            var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1)); present.Start(now.AddDays(-1), now.AddDays(1)); present.Complete(now);
            var decision = new ClientQuotationDecision(project.Id, quote.Id, present.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null);
            var accounting = new ProjectTask(project.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now); accounting.StartUntracked(now); accounting.Complete(now);
            var handoff = new AccountingHandoff(project.Id, decision.Id, quote.Id, 1, accounting.Id, now, "advisor", null, null);
            var task = new ProjectTask(project.Id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, "installer", now);
            db.AddRange(version, catalog, quote, present, decision, accounting, handoff, new InitialDeposit(handoff, 50, now, now, null, null, "accountant", null, null), task);

            task.StartUntracked(now);
            var checklist = new MeasurementChecklist(project.Id, quote.Id, 1, task.Id, now, actor.Subject);
            db.Add(checklist);
            var originals = new List<(CommercialQuotationItem Item, MeasurementStructure Structure)>();
            // Create in descending row order, with nonnumeric/repeated labels and deliberately reversed GUID order.
            for (var index = count; index >= 1; index--)
            {
                var section = index % 2 == 0 ? CommercialItemSection.Additional : CommercialItemSection.Main;
                var values = new CatalogItemValues("V-X", "Room", "Window", null, null, null, null, 1.2m, 2, 2.4m, index % 3 + 1);
                var source = new CommercialCatalogItem(catalog.Id, section, index * 10, values);
                var item = new CommercialQuotationItem(quote.Id, source.Id, section, index * 10, values, 10, 100);
                db.AddRange(source, item);
                db.Entry(item).Property(x => x.Id).CurrentValue = Guid.Parse($"{count-index+1:x8}-0000-0000-0000-{Guid.NewGuid().ToString("N")[20..]}");
                for (var unit = item.Quantity; unit >= 1; unit--)
                {
                    var row = MeasurementStructure.Quoted(checklist, item, unit, now.AddMinutes(-index), actor.Subject);
                    row.RecordActual(1.1m, 2.1m, "Site system", now, actor.Subject);
                    row.Measure(true, now, actor.Subject);
                    if (index == count && unit == 1 && count > 1) row.Retire(now, actor.Subject);
                    originals.Add((item, row)); db.Add(row);
                }
            }
            // Earlier than quoted creation dates: these must still follow every original unit.
            var additions = Enumerable.Range(0, 3).Select(i => MeasurementStructure.Added(checklist, "Added", 3, 4, null, now.AddDays(-2).AddMinutes(i == 0 ? 1 : 0), actor.Subject)).ToArray();
            foreach (var row in additions) { row.Measure(true, now, actor.Subject); db.Add(row); }
            additions[0].Retire(now, actor.Subject);
            var document = new ProjectDocument(Guid.NewGuid(), project.Id, "synthetic.pdf", "application/pdf", 1, Guid.NewGuid(), now, actor.Subject, null, null, isMeasurementDocument: true);
            db.Add(document);
            // A newer quotation must not become the source of ordering or structure identity.
            var newerVersion = new BudgetQuotationVersion(project.Id, 2, "synthetic-new.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
            var newerCatalog = new CommercialCatalog(newerVersion.Id);
            db.AddRange(newerVersion, newerCatalog, new CommercialQuotation(project.Id, newerVersion.Id, newerCatalog.Id, "synthetic-new.xlsx", 200, "head", null, null));
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var expected = originals.OrderBy(x => x.Item.Section).ThenBy(x => x.Item.SourceRow).ThenBy(x => x.Structure.UnitOrdinal).ThenBy(x => x.Structure.Id).Select(x => x.Structure.Id)
                .Concat(additions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.Id)).ToArray();
            async Task<string> StoredRows() => System.Text.Json.JsonSerializer.Serialize(await db.MeasurementStructures.AsNoTracking().Where(x => x.ProjectId == project.Id).OrderBy(x => x.Id).ToArrayAsync());
            async Task<string> StoredQuotes() => System.Text.Json.JsonSerializer.Serialize(await db.CommercialQuotationItems.AsNoTracking().Where(x => x.CommercialQuotationId == quote.Id).OrderBy(x => x.Id).ToArrayAsync());
            var before = await StoredRows(); var quotationBefore = await StoredQuotes();
            var read = (await Store(db).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
            Assert.Equal(expected, read.Structures.Select(x => x.Id));
            Assert.Equal(originals.Count + 3, read.Structures.Count); Assert.Empty(read.History);
            await using (var reopened = Db())
            {
                var reloaded = (await Store(reopened).ReadAsync(project.Id, new("manager", SystemRole.Management), default))!;
                Assert.Equal(read.Structures, reloaded.Structures); Assert.Equal(read.Summary, reloaded.Summary); Assert.Empty(reloaded.History);
            }
            Assert.Equal(before, await StoredRows()); Assert.Equal(quotationBefore, await StoredQuotes());
            // Valid FK, wrong historical catalog: must not render another quotation's image.
            var wrongSource = new CommercialCatalogItem(newerCatalog.Id, originals[0].Item.Section, originals[0].Item.SourceRow,
                new CatalogItemValues("V-X", "Room", "Window", null, null, null, null, 1, 1, 1, 1));
            db.Add(wrongSource); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var chosen = originals[0].Item;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialQuotationItems\" SET \"CommercialCatalogItemId\" = {wrongSource.Id} WHERE \"Id\" = {chosen.Id}");
            try { await Error("MeasurementReportUnavailable", () => Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), 0, "Submit", DocumentIds: [document.Id]), actor, default)); }
            finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialQuotationItems\" SET \"CommercialCatalogItemId\" = {chosen.CommercialCatalogItemId} WHERE \"Id\" = {chosen.Id}"); }
            Assert.False(await db.MeasurementReports.AnyAsync(r => r.ProjectId == project.Id));
            await Store(db).ExecuteAsync(project.Id, new(Guid.NewGuid(), 0, "Submit", DocumentIds: [document.Id]), actor, default);
            await using (var reopened = Db())
            {
                var submitted = (await Store(reopened).ReadAsync(project.Id, new("installer", SystemRole.Installation), default))!;
                Assert.Equal(expected, submitted.Snapshot!.Structures.Select(x => x.Id));
                Assert.Equal(read.Structures, submitted.Snapshot.Structures);
                Assert.Equal(quote.Id, submitted.Snapshot.CommercialQuotationId);
                Assert.Equal(read.Summary, submitted.Summary); Assert.Single(submitted.History, x => x.Action == "Submit");
            }
            Assert.Equal(before, await StoredRows()); Assert.Equal(quotationBefore, await StoredQuotes());
        }
    }
    private static InstallationTechnicalDetails CompleteTechnical() => new("NotApplicable", null, "Outside", "NotApplicable", null, null, false, null);
    private static async Task Error(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<DocumentException>(action)).Code);
    private static async IAsyncEnumerable<DocumentUpload> Uploads(params string[] names)
    {
        for (var i = 0; i < names.Length; i++) { using var stream = new MemoryStream([1, 2, 3]); yield return new($"file-{i}", names[i], "application/octet-stream", null, stream); }
        await Task.CompletedTask;
    }
    private static async IAsyncEnumerable<DocumentUpload> Replacing(Guid id)
    { using var stream = new MemoryStream([1]); yield return new("file-0", "replace.pdf", null, id, stream); await Task.CompletedTask; }
    private sealed class FailSave(bool fail) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (fail) throw new InvalidOperationException("Synthetic pre-commit failure"); return ValueTask.FromResult(result); }
    }
}
