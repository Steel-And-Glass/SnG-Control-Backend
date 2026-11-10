using System.Data;
using SnG.Control.Infrastructure.Measurements;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Application.Files;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed partial class EfMeasurementStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications, IFileStorage files, SnG.Control.Application.Identity.IEngineeringAssigneeIdentity? engineering = null, IncrementalMeasurementActivation? activation = null) : IMeasurementStore
{
    public async Task<MeasurementState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        if (scope.Role is not (SystemRole.Installation or SystemRole.Management)) return null;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token)) return null;
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id && t.Type == ProjectTaskType.TakeMeasurements).ToArrayAsync(token);
        if (tasks.Length != 1 || !await db.InitialDeposits.AnyAsync(d => d.ProjectId == id, token)) return null;
        var task = tasks[0];
        var checklist = await db.MeasurementChecklists.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == id, token);
        var rows = await OrderedStructures(db.MeasurementStructures.Where(x => x.ProjectId == id)).ToArrayAsync(token);
        var history = await db.MeasurementEvents.AsNoTracking().Where(x => x.ProjectId == id).OrderBy(x => x.Revision).ToArrayAsync(token);
        var documents = await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == id && d.IsMeasurementDocument && d.RemovedAt == null).OrderBy(d => d.UploadedAt).ThenBy(d => d.Id).ToArrayAsync(token);
        var views = await StructureViewsAsync(rows, token);
        var deliveries = await DeliveriesAsync(id, token);
        await tx.CommitAsync(token);
        return new(id, task.Id, task.Status.ToString(), task.AssignedToGoogleSubject!,
            scope.Role == SystemRole.Installation && task.AssignedToGoogleSubject == scope.Subject && task.Status != ProjectTaskStatus.Completed && checklist?.SubmissionId is null,
            checklist?.Id, checklist?.Revision ?? 0, MeasurementSummary.From(rows), views,
            documents.Select(d => DocumentResponse.From(d, new HashSet<Guid>())).ToArray(),
            history.Select(e => new MeasurementHistory(e.Id, e.Action, e.StructureId, e.At, new(e.ActorEmail, e.ActorName), e.Revision, e.DetailsJson is null ? null : JsonSerializer.Deserialize<MeasurementDetailsChange>(e.DetailsJson), e.TechnicalChangeJson is null ? null : JsonSerializer.Deserialize<MeasurementTechnicalChange>(e.TechnicalChangeJson), e.SketchChangeJson is null ? null : JsonSerializer.Deserialize<MeasurementSketchChange>(e.SketchChangeJson), e.AssessmentChangeJson is null ? null : JsonSerializer.Deserialize<InstallationAssessmentChange>(e.AssessmentChangeJson))).ToArray(),
            checklist?.SubmissionId, checklist?.SubmittedAt, checklist?.SnapshotJson is { } json ? JsonSerializer.Deserialize<MeasurementSnapshot>(json) : null, checklist?.IsIncremental ?? false, deliveries);
    }
    private async Task<MeasurementSketchVersion?> AuthorizedSketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token)
    {
        if (scope.Role is not (SystemRole.Installation or SystemRole.Management) || !await db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token)) return null;
        return await db.MeasurementSketchVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId && v.ProjectId == id && v.StructureId == structureId, token);
    }
    public async Task<SketchRead?> ReadSketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token)
    {
        var v = await AuthorizedSketchAsync(id, structureId, versionId, scope, token);
        return v is null ? null : new(v.Id, v.StructureId, v.Revision, JsonSerializer.Deserialize<SketchDrawing>(v.DrawingJson)!, v.WebpSha256);
    }
    public async Task<byte[]?> ReadSketchImageAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token)
    {
        var v = await AuthorizedSketchAsync(id, structureId, versionId, scope, token);
        if (v is null) return null;
        var bytes = await files.ReadAsync(v.StorageKey, token);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != v.WebpSha256) throw new DocumentException(409, "MeasurementSketchIntegrity");
        return bytes;
    }
    private IQueryable<MeasurementStructure> OrderedStructures(IQueryable<MeasurementStructure> structures) =>
        from structure in structures.AsNoTracking()
        join item in db.CommercialQuotationItems.AsNoTracking() on structure.CommercialQuotationItemId equals item.Id into items
        from item in items.DefaultIfEmpty()
        orderby structure.CommercialQuotationItemId == null,
            (CommercialItemSection?)item.Section, (int?)item.SourceRow,
            structure.CommercialQuotationItemId == null ? (int?)null : structure.UnitOrdinal,
            structure.CommercialQuotationItemId == null ? (DateTime?)structure.CreatedAt : null,
            structure.Id
        select structure;

    private async Task<MeasurementStructureView[]> StructureViewsAsync(MeasurementStructure[] rows, CancellationToken token)
    {
        var itemIds = rows.Where(s => s.CommercialQuotationItemId != null).Select(s => s.CommercialQuotationItemId!.Value).Distinct().ToArray();
        var images = await (from item in db.CommercialQuotationItems.AsNoTracking()
                            join extraction in db.CommercialCatalogItemExtractions.AsNoTracking() on item.CommercialCatalogItemId equals extraction.CatalogItemId
                            where itemIds.Contains(item.Id) && extraction.ImageStatus == CatalogItemImageStatus.Stored && extraction.ImageStorageKey != null && extraction.ImageContentType != null
                            select new { item.Id, extraction.CatalogItemId }).ToDictionaryAsync(x => x.Id, x => x.CatalogItemId, token);
        return rows.Select(s => MeasurementStructureView.From(s) with { ImageCatalogItemId = s.CommercialQuotationItemId is { } itemId && images.TryGetValue(itemId, out var imageId) ? imageId : null }).ToArray();
    }
    private async Task<ProjectTask> LockAsync(Guid id, string subject, CancellationToken token)
    {
        if (!await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().AnyAsync(token)) throw new DocumentException(404, "ProjectNotFound");
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var candidates = tasks.Where(t => t.Type == ProjectTaskType.TakeMeasurements).ToArray();
        if (candidates.Length != 1 || !await db.InitialDeposits.AnyAsync(d => d.ProjectId == id, token)) throw new DocumentException(409, "MeasurementUnavailable");
        var task = candidates[0];
        if (task.AssignedToGoogleSubject != subject) throw new DocumentException(403, "MeasurementAccessDenied");
        return task;
    }
    public async Task InitializeAsync(Guid id, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var task = await LockAsync(id, actor.Subject, token);
        if (await db.MeasurementChecklists.AnyAsync(c => c.ProjectId == id, token)) return;
        if (task.Status != ProjectTaskStatus.Pending) throw new DocumentException(409, "MeasurementUnavailable");
        var deposit = await db.InitialDeposits.AsNoTracking().SingleAsync(d => d.ProjectId == id, token);
        var handoff = await db.AccountingHandoffs.AsNoTracking().SingleAsync(h => h.Id == deposit.AccountingHandoffId, token);
        var items = await db.CommercialQuotationItems.AsNoTracking().Where(i => i.CommercialQuotationId == deposit.CommercialQuotationId).ToArrayAsync(token);
        if (items.Length == 0 || items.Any(i => i.Quantity <= 0)) throw new DocumentException(409, "MeasurementCatalogInvalid");
        var now = timing.UtcNow;
        var createdAt = await db.Projects.Where(p => p.Id == id).Select(p => p.CreatedAt).SingleAsync(token);
        var checklist = new MeasurementChecklist(id, deposit.CommercialQuotationId, handoff.QuotationRound, task.Id, now, actor.Subject, activation?.Includes(createdAt) == true);
        var entities = new List<object> { checklist };
        foreach (var item in items)
            for (var unit = 0; unit < item.Quantity; unit++) entities.Add(MeasurementStructure.Quoted(checklist, item, checked(unit + 1), now, actor.Subject));
        entities.Add(new MeasurementEvent(checklist, Guid.NewGuid(), "", "Initialize", null, now, actor.Subject, actor.Email, actor.Name));
        db.Attach(task); var before = db.Entry(task).CurrentValues.Clone();
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            task.StartUntracked(now); db.AddRange(entities); staged = await notifications.StageAsync([task], now, token);
            await db.SaveChangesAsync(token); if (!checklist.IsIncremental) await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token);
        }
        catch
        {
            staged?.RestoreTracking(); foreach (var entity in entities) db.Entry(entity).State = EntityState.Detached;
            db.Entry(task).CurrentValues.SetValues(before); db.Entry(task).State = EntityState.Detached; throw;
        }
    }
    public async Task ExecuteAsync(Guid id, MeasurementCommand command, DecisionActor actor, CancellationToken token)
    {
        // Render a proposed immutable snapshot outside write locks. The transaction below revalidates
        // the exact checklist revision, documents and structure projection before committing both artifacts.
        PreparedReport? prepared = command.Action == "Submit" ? await PrepareReportAsync(id, command, actor, token) : null;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var task = await LockAsync(id, actor.Subject, token);
        var checklist = await db.MeasurementChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token) ?? throw new DocumentException(409, "MeasurementUnavailable");
        var normalized = command with { DocumentIds = command.DocumentIds?.Order().ToArray() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
        var replay = await db.MeasurementEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ChecklistId == checklist.Id && e.RequestKey == command.RequestKey, token);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint || replay.ActorSubject != actor.Subject) throw new DocumentException(409, "MeasurementRequestConflict");
            return;
        }
        if (checklist.SubmissionId is not null || task.Status != ProjectTaskStatus.InProgress) throw new DocumentException(409, "MeasurementsLocked");
        if (checklist.Revision != command.ExpectedRevision) throw new DocumentException(409, "MeasurementRevisionConflict");
        var rows = await OrderedStructures(db.MeasurementStructures.Where(s => s.ChecklistId == checklist.Id)).ToArrayAsync(token);
        string? stagedSketchKey = null; bool commitAttempted = false;
        var now = timing.UtcNow; var added = new List<object>(); var changed = new List<(object Entity, Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues Before)>();
        void Track(object entity) { db.Attach(entity); changed.Add((entity, db.Entry(entity).CurrentValues.Clone())); }
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            Track(checklist); Guid? structureId = command.StructureId; MeasurementDetailsChange? details = null; MeasurementTechnicalChange? technical = null; MeasurementSketchChange? sketch = null; InstallationAssessmentChange? assessment = null;
            if (command.Action == "Submit")
            {
                var summary = MeasurementSummary.From(rows);
                if (summary.Active == 0 || summary.Pending != 0) throw new DocumentException(409, "MeasurementsIncomplete");
                var docs = await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == id && d.IsMeasurementDocument && d.RemovedAt == null && d.ReplacedAt == null).ToArrayAsync(token);
                if (docs.Length == 0 || command.DocumentIds is null || !docs.Select(d => d.Id).Order().SequenceEqual(command.DocumentIds.Order())) throw new DocumentException(409, "MeasurementDocumentsChanged");
                if (prepared == null) throw new DocumentException(409, "MeasurementReportUnavailable");
                var finalSnapshot = new MeasurementSnapshot(checklist.CommercialQuotationId, checklist.QuotationRound, summary, await StructureViewsAsync(rows, token), docs.Select(d => d.Id).Order().ToArray(), prepared.Snapshot.Report);
                var snapshotJson = JsonSerializer.Serialize(finalSnapshot);
                if (snapshotJson != prepared.Json) throw new DocumentException(409, "MeasurementRevisionConflict");
                checklist.Submit(prepared.Snapshot.Report!.SubmittedAt, actor.Subject, snapshotJson, prepared.Snapshot.Report.SubmissionId);
                added.Add(new MeasurementReport(checklist, prepared.Bytes, Hash(prepared.Bytes), Hash(Encoding.UTF8.GetBytes(snapshotJson))));
                added.AddRange(docs.Select(d => new MeasurementSubmissionDocument(checklist, d)));
                var next = EfEngineeringStore.Create(checklist, finalSnapshot, engineering?.Subject ?? "", now, actor);
                added.AddRange(next.Entities); db.Add(next.Task);
                Track(task); task.Complete(now); staged = await notifications.StageAsync([task, next.Task], now, token);
            }
            else
            {
                if (command.Action == "Add")
                {
                    var row = MeasurementStructure.Added(checklist, command.Description!, command.Width!.Value, command.Height!.Value, command.Note, now, actor.Subject);
                    row.RecordActual(command.Width.Value, command.Height.Value, command.Description!, now, actor.Subject);
                    details = new(new(null, null, null), MeasurementActualValues.From(row));
                    added.Add(row); structureId = row.Id;
                }
                else
                {
                    var row = rows.SingleOrDefault(r => r.Id == command.StructureId) ?? throw new DocumentException(404, "MeasurementStructureNotFound");
                    if (row.RetiredAt is not null && command.Action != "Reinstate") throw new DocumentException(409, "MeasurementStructureRetired");
                    if (await db.Set<SnG.Control.Domain.Entities.Incremental.MeasurementSubmissionMember>().AnyAsync(m => m.ProjectId == id && m.StructureId == row.Id && m.EvidenceVersion == 1, token)) throw new DocumentException(409, "MeasurementsLocked");
                    Track(row);
                    if (command.Action is "ResolveComparison" or "RecordModification")
                    {
                        var beforeComparison = row.Comparison; var beforeModification = row.Modification;
                        try
                        {
                            if (command.Action == "ResolveComparison") row.ResolveComparison(command.Comparison!.Resolution, command.Comparison.Reason, now, actor.Subject);
                            else row.RecordModification(command.Modification!.Type, command.Modification.Reason, command.Modification.Description, command.Modification.SketchVersionId, now, actor.Subject);
                        }
                        catch (ArgumentException) { throw new DocumentException(400, "InvalidMeasurementAssessment"); }
                        catch (InvalidOperationException) { throw new DocumentException(409, "MeasurementsIncomplete"); }
                        assessment = new(beforeComparison, row.Comparison, beforeModification, row.Modification);
                    }
                    else if (command.Action == "RecordTechnical")
                    {
                        var before = row.Technical;
                        try { row.RecordTechnical(command.Technical!, now, actor.Subject); }
                        catch (ArgumentException) { throw new DocumentException(400, "InvalidMeasurementTechnical"); }
                        catch (InvalidOperationException) { throw new DocumentException(409, "MeasurementsIncomplete"); }
                        if (row.MeasuredAt != null && !row.IsTechnicallyComplete) throw new DocumentException(409, "MeasurementsIncomplete");
                        technical = new(before, row.Technical);
                    }
                    else if (command.Action == "RecordSketch")
                    {
                        MeasurementSketch.Validate(command.Sketch!);
                        var previous = await db.MeasurementSketchVersions.AsNoTracking().Where(v => v.StructureId == row.Id).OrderByDescending(v => v.Revision).FirstOrDefaultAsync(token);
                        if (previous != null && now < previous.CreatedAt.AddSeconds(2)) throw new DocumentException(429, "MeasurementSketchRateLimit");
                        var version = new MeasurementSketchVersion(row, checklist.Revision + 1, JsonSerializer.Serialize(command.Sketch!.Drawing), Convert.ToHexString(SHA256.HashData(command.Sketch.Webp)), now, actor.Subject);
                        sketch = new(row.SketchVersionId, version.Id); row.SetSketch(version.Id, now, actor.Subject);
                        added.Add(version); stagedSketchKey = version.StorageKey;
                        await files.SaveAsync(version.StorageKey, command.Sketch.Webp, token);
                    }
                    else if (command.Action == "RecordActual")
                    {
                        var before = MeasurementActualValues.From(row);
                        try {
                            row.RecordActual(command.ActualWidth!.Value, command.ActualHeight!.Value, command.ActualSystemText!, now, actor.Subject);
                            row.RecordSiteDetails(command.MeasurementType, command.ActualLocation, command.InstallationObservations, now, actor.Subject);
                        }
                        catch (InvalidOperationException) { throw new DocumentException(409, "MeasurementsIncomplete"); }
                        catch (ArgumentException) { throw new DocumentException(400, "InvalidMeasurementTechnical"); }
                        if (row.MeasuredAt != null && !row.IsTechnicallyComplete) throw new DocumentException(409, "MeasurementsIncomplete");
                        details = new(before, MeasurementActualValues.From(row));
                    }
                    else if (command.Action == "Measure")
                    {
                        if (command.Measured == true && !row.IsTechnicallyComplete) throw new DocumentException(409, "MeasurementsIncomplete");
                        row.Measure(command.Measured!.Value, now, actor.Subject);
                    }
                    else if (command.Action == "Retire") row.Retire(now, actor.Subject);
                    else if (command.Action == "Reinstate")
                    {
                        if (row.RetiredAt is null) throw new DocumentException(409, "MeasurementStructureNotRetired");
                        row.Reinstate(now, actor.Subject);
                    }
                    else throw new DocumentException(400, "InvalidMeasurementCommand");
                }
                checklist.Advance();
            }
            added.Add(new MeasurementEvent(checklist, command.RequestKey, fingerprint, command.Action == "Measure" && command.Measured == false ? "Unmeasure" : command.Action, structureId, now, actor.Subject, actor.Email, actor.Name, details is null ? null : JsonSerializer.Serialize(details), technical is null ? null : JsonSerializer.Serialize(technical), sketch is null ? null : JsonSerializer.Serialize(sketch), assessment is null ? null : JsonSerializer.Serialize(assessment)));
            db.AddRange(added); await db.SaveChangesAsync(token); if (!checklist.IsIncremental) await IncrementalLegacyProjection.SyncAsync(db, id, token); commitAttempted = true; await tx.CommitAsync(token);
        }
        catch
        {
            if (stagedSketchKey != null && !commitAttempted)
            {
                // Delete only after a known rollback. Unknown commit outcome preserves immutable evidence.
                await tx.RollbackAsync(CancellationToken.None);
                await files.DeleteAsync(stagedSketchKey, CancellationToken.None);
            }
            staged?.RestoreTracking(); foreach (var entity in added) db.Entry(entity).State = EntityState.Detached;
            foreach (var (entity, before) in changed) { db.Entry(entity).CurrentValues.SetValues(before); db.Entry(entity).State = EntityState.Detached; }
            throw;
        }
    }
    private sealed record PreparedReport(MeasurementSnapshot Snapshot, string Json, byte[] Bytes);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private async Task<PreparedReport?> PrepareReportAsync(Guid id, MeasurementCommand command, DecisionActor actor, CancellationToken token)
    {
        var scope = new ProjectReadScope(actor.Subject, SystemRole.Installation);
        var state = await ReadAsync(id, scope, token) ?? throw new DocumentException(404, "ProjectNotFound");
        if (state.IsIncremental) throw new DocumentException(409, "MeasurementsLocked");
        if (state.SubmissionId != null) return null; // exact command replay is checked inside the transaction
        if (string.IsNullOrWhiteSpace(engineering?.Subject)) throw new DocumentException(503, "EngineeringAssigneeNotConfigured");
        if (!state.CanModify) throw new DocumentException(403, "MeasurementAccessDenied");
        if (state.Revision != command.ExpectedRevision) throw new DocumentException(409, "MeasurementRevisionConflict");
        if (state.Summary.Active == 0 || state.Summary.Pending != 0) throw new DocumentException(409, "MeasurementsIncomplete");
        var docs = state.Documents.Where(d => d.IsCurrent).Select(d => d.Id).Order().ToArray();
        if (docs.Length == 0 || command.DocumentIds == null || !docs.SequenceEqual(command.DocumentIds.Order())) throw new DocumentException(409, "MeasurementDocumentsChanged");
        var checklist = await db.MeasurementChecklists.AsNoTracking().SingleAsync(c => c.Id == state.ChecklistId, token);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == id, token);
        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == project.ClientId, token);
        var header = new MeasurementReportHeader(Guid.NewGuid(), 1, project.Name, project.SgCode, client.Name, timing.UtcNow, actor.Name ?? "Instalaci?n asignada", state.History.Where(e => e.Action == "Reinstate").Select(e => e.StructureId).Distinct().Count());
        var snapshot = new MeasurementSnapshot(checklist.CommercialQuotationId, checklist.QuotationRound, state.Summary, state.Structures, docs, header);
        var sketches = new Dictionary<Guid, byte[]>();
        foreach (var row in snapshot.Structures)
            foreach (var version in new[] { row.SketchVersionId, row.Modification?.SketchVersionId }.Where(v => v != null).Select(v => v!.Value).Distinct())
            {
                var bytes = await ReadSketchImageAsync(id, row.Id, version, scope, token) ?? throw new DocumentException(409, "MeasurementReportUnavailable");
                sketches.Add(version, bytes);
            }
        try
        {
            var images = await ReadQuotationImagesAsync(id, snapshot, token);
            return new(snapshot, JsonSerializer.Serialize(snapshot), new InstallationPdfRenderer().Render(new(snapshot, sketches, images), token));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { throw new DocumentException(409, "MeasurementReportUnavailable"); }
    }
    // Resolve only the frozen quotation's items, never the latest project quotation or display number.
    private async Task<IReadOnlyDictionary<Guid, byte[]>> ReadQuotationImagesAsync(Guid projectId, MeasurementSnapshot snapshot, CancellationToken token)
    {
        var ids = snapshot.Structures.Select(s => s.ItemId).OfType<Guid>().Distinct().ToArray();
        var links = await (from item in db.CommercialQuotationItems.AsNoTracking()
                           join quote in db.CommercialQuotations.AsNoTracking() on item.CommercialQuotationId equals quote.Id
                           join source in db.CommercialCatalogItems.AsNoTracking() on item.CommercialCatalogItemId equals source.Id
                           join catalog in db.CommercialCatalogs.AsNoTracking() on source.CommercialCatalogId equals catalog.Id
                           join budget in db.BudgetQuotationVersions.AsNoTracking() on catalog.BudgetQuotationVersionId equals budget.Id
                           where ids.Contains(item.Id) && quote.Id == snapshot.CommercialQuotationId && quote.ProjectId == projectId
                               && quote.CommercialCatalogId == catalog.Id && quote.BudgetQuotationVersionId == budget.Id && budget.ProjectId == projectId
                               && item.Section == source.Section && item.SourceRow == source.SourceRow
                           select new { item.Id, sourceId = source.Id, budgetId = budget.Id }).ToArrayAsync(token);
        if (links.Length != ids.Length) throw new DocumentException(409, "MeasurementReportUnavailable");
        var sourceIds = links.Select(l => l.sourceId).ToArray();
        var extractions = await db.CommercialCatalogItemExtractions.AsNoTracking()
            .Where(e => sourceIds.Contains(e.CatalogItemId) && e.ImageStatus == CatalogItemImageStatus.Stored && e.ImageStorageKey != null)
            .ToDictionaryAsync(e => e.CatalogItemId, token);
        var result = new Dictionary<Guid, byte[]>();
        long total = 0;
        foreach (var link in links)
        {
            if (!extractions.TryGetValue(link.sourceId, out var extraction)) continue;
            var stem = $"projects/{projectId:D}/budget-quotations/{link.budgetId:D}/images/{link.sourceId:D}";
            if (!new[] { ".png", ".jpg", ".gif", ".bmp", ".tiff", ".bin" }.Any(ext => extraction.ImageStorageKey == stem + ext))
                throw new DocumentException(409, "MeasurementReportUnavailable");
            try
            {
                byte[] bytes;
                if (files is IStreamingFileStorage streaming)
                {
                    await using var input = await streaming.OpenReadAsync(extraction.ImageStorageKey!, token);
                    using var output = new MemoryStream();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) != 0)
                    {
                        if (output.Length + read > InstallationPdfRenderer.MaxImageBytes) throw new InvalidOperationException("Report image exceeds byte limit.");
                        output.Write(buffer, 0, read);
                    }
                    bytes = output.ToArray();
                }
                else bytes = await files.ReadAsync(extraction.ImageStorageKey!, token);
                total += bytes.Length;
                if (bytes.Length > InstallationPdfRenderer.MaxImageBytes || total > 128L * 1024 * 1024)
                    throw new InvalidOperationException("Report images exceed byte limit.");
                result.Add(link.Id, bytes);
            }
            catch (FileNotFoundException) { /* Explicit unavailable-image caption in the report. */ }
            catch (DirectoryNotFoundException) { /* Historical file absent; never substitute another item. */ }
        }
        return result;
    }
    public async Task<MeasurementReportFile?> ReadReportAsync(Guid id, Guid submissionId, ProjectReadScope scope, CancellationToken token)
    {
        if (scope.Role is not (SystemRole.Installation or SystemRole.Management or SystemRole.Engineering)) return null;
        var projectName = await db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).Where(p => p.Id == id).Select(p => p.Name).SingleOrDefaultAsync(token);
        if (projectName is null) return null;
        var native = await ReadIncrementalReportAsync(id, submissionId, projectName, token);
        if (native != null) return native;
        var report = await db.MeasurementReports.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.SubmissionId == submissionId, token);
        if (report == null) return null;
        var snapshot = await db.MeasurementChecklists.AsNoTracking().Where(c => c.Id == report.ChecklistId && c.SubmissionId == submissionId).Select(c => c.SnapshotJson).SingleAsync(token);
        using var expected = JsonDocument.Parse(report.SnapshotCanonical); using var actual = JsonDocument.Parse(snapshot!);
        if (Hash(report.Pdf) != report.PdfSha256 || Hash(Encoding.UTF8.GetBytes(report.SnapshotCanonical)) != report.SnapshotSha256 || !JsonElement.DeepEquals(expected.RootElement, actual.RootElement)) throw new DocumentException(409, "MeasurementReportIntegrity");
        return new(report.Pdf, MeasurementReportName.ForProject(projectName));
    }
    public static async Task RequireUploadAsync(SnGControlDbContext db, Guid id, IReadOnlyList<ProjectTask> tasks, string subject, CancellationToken token)
    {
        var task = tasks.SingleOrDefault(t => t.Type == ProjectTaskType.TakeMeasurements);
        if (task is null || task.AssignedToGoogleSubject != subject) throw new DocumentException(403, "MeasurementAccessDenied");
        var checklist = await db.MeasurementChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token);
        if (checklist is null || checklist.SubmissionId is not null || task.Status != ProjectTaskStatus.InProgress) throw new DocumentException(409, "MeasurementsLocked");
    }
}
