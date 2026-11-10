using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Files;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfEngineeringStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications,
    IStreamingFileStorage files, IEngineeringAssigneeIdentity engineering, IRoleResolver? roles = null) : IEngineeringStore
{
    public const int MaxPdfBytes = 25 * 1024 * 1024;
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static string Fingerprint(object value) => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    private static MeasurementStructureView Source(EngineeringStructure row) => JsonSerializer.Deserialize<MeasurementStructureView>(row.SnapshotJson)!;
    public static (ProjectTask Task, IReadOnlyList<object> Entities) Create(MeasurementChecklist source, MeasurementSnapshot snapshot, string subject, DateTime at, DecisionActor actor, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new DocumentException(503, "EngineeringAssigneeNotConfigured");
        if (source.SubmissionId is null || snapshot.Structures.Count == 0 || snapshot.Structures.Select(s => s.Id).Distinct().Count() != snapshot.Structures.Count)
            throw new DocumentException(409, "EngineeringSourceInvalid");
        var rows = snapshot.Structures.Where(s => s.RetiredAt == null).ToArray();
        if (rows.Length == 0) throw new DocumentException(409, "EngineeringSourceInvalid");
        var task = new ProjectTask(source.ProjectId, OperationalArea.Engineering, ProjectTaskType.PrepareOpeningRecord, subject, at);
        task.StartUntracked(at);
        var checklist = new EngineeringChecklist(source.ProjectId, source.Id, source.SubmissionId.Value, task.Id, at);
        var entities = new List<object> { task, checklist };
        entities.AddRange(rows.Select((s, i) => new EngineeringStructure(checklist, s.Id, i, JsonSerializer.Serialize(s))));
        entities.Add(new EngineeringEvent(checklist, Guid.NewGuid(), "", "Initialize", null, at, actor.Subject, actor.Email, actor.Name, reason));
        return (task, entities);
    }
    private Task<bool> CanRead(Guid id, ProjectReadScope scope, CancellationToken token) =>
        db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(p => p.Id == id, token);
    public async Task<EngineeringState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await CanRead(id, scope, token)) return null;
        var c = await db.EngineeringChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token);
        if (c == null) return null;
        var task = await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == c.TaskId, token);
        var rows = await db.EngineeringStructures.AsNoTracking().Where(r => r.ChecklistId == c.Id).OrderBy(r => r.Position).ToArrayAsync(token);
        var docs = await (from a in db.EngineeringActas.AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on a.DocumentId equals d.Id
                          where a.ChecklistId == c.Id orderby a.Version select new { a, d }).ToArrayAsync(token);
        var history = await db.EngineeringEvents.AsNoTracking().Where(e => e.ChecklistId == c.Id).OrderBy(e => e.Revision).ToArrayAsync(token);
        var completed = rows.Count(r => r.Completed);
        await tx.CommitAsync(token);
        return new(id, c.Id, c.SubmissionId, task.Id, task.Status.ToString(), task.AssignedToGoogleSubject!,
            scope.Role == SystemRole.Engineering && scope.Subject == task.AssignedToGoogleSubject && c.CompletedAt == null && task.Status == ProjectTaskStatus.InProgress,
            task.StartedAt!.Value, c.CompletedAt, c.Revision, rows.Length, completed, rows.Length - completed, rows.Length == 0 ? 0 : decimal.Round(100m * completed / rows.Length, 2),
            rows.Select(r => new EngineeringRow(Source(r), r.Revision, r.Completed, r.ChangedAt)).ToArray(),
            docs.Select(d => new EngineeringDocument(DocumentResponse.From(d.d, new HashSet<Guid>()), d.a.Version, d.a.Sha256)).ToArray(),
            c.CurrentDocumentId, history.Select(e => new EngineeringHistory(e.Action, e.StructureId, e.At, new(e.ActorEmail, e.ActorName), e.Revision, e.Reason)).ToArray());
    }
    private async Task<ProjectTask[]> LockProject(Guid id, CancellationToken token)
    {
        if (!await db.Projects.FromSqlInterpolated($""" SELECT * FROM "Projects" WHERE "Id" = {id} FOR UPDATE """).AsNoTracking().AnyAsync(token))
            throw new DocumentException(404, "ProjectNotFound");
        return await db.ProjectTasks.FromSqlInterpolated($""" SELECT * FROM "ProjectTasks" WHERE "ProjectId" = {id} FOR UPDATE """).AsNoTracking().ToArrayAsync(token);
    }
    private async Task<(EngineeringChecklist C, ProjectTask Task)> Lock(Guid id, DecisionActor actor, CancellationToken token)
    {
        var tasks = await LockProject(id, token);
        var c = await db.EngineeringChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token) ?? throw new DocumentException(409, "EngineeringUnavailable");
        var task = tasks.Single(t => t.Id == c.TaskId);
        if (task.AssignedToGoogleSubject != actor.Subject || engineering.Subject != actor.Subject) throw new DocumentException(403, "EngineeringAccessDenied");
        return (c, task);
    }
    private async Task<bool> Replay(EngineeringChecklist c, Guid key, string fingerprint, DecisionActor actor, CancellationToken token)
    {
        var old = await db.EngineeringEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ChecklistId == c.Id && e.RequestKey == key, token);
        if (old == null) return false;
        if (old.Fingerprint != fingerprint || old.ActorSubject != actor.Subject) throw new DocumentException(409, "EngineeringRequestConflict");
        return true;
    }
    private static void Open(EngineeringChecklist c, ProjectTask task)
    { if (c.CompletedAt != null || task.Status != ProjectTaskStatus.InProgress) throw new DocumentException(409, "EngineeringLocked"); }
    private static void Revision(long actual, long expected)
    { if (actual != expected) throw new DocumentException(409, "EngineeringRevisionConflict"); }
    public async Task CheckAsync(Guid id, Guid structureId, EngineeringCheck command, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var (c, task) = await Lock(id, actor, token); var fingerprint = Fingerprint(new { action = "Check", structureId, command });
        if (await Replay(c, command.RequestKey, fingerprint, actor, token)) return;
        Open(c, task);
        var row = await db.EngineeringStructures.AsNoTracking().SingleOrDefaultAsync(r => r.ChecklistId == c.Id && r.StructureId == structureId, token) ?? throw new DocumentException(404, "EngineeringStructureNotFound");
        Revision(row.Revision, command.ExpectedRevision); var now = timing.UtcNow;
        db.Attach(c); db.Attach(row);
        row.SetCompleted(command.Completed, now, actor.Subject); c.Advance();
        var e = new EngineeringEvent(c, command.RequestKey, fingerprint, command.Completed ? "Check" : "Uncheck", structureId, now, actor.Subject, actor.Email, actor.Name);
        try { db.Add(e); await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token); }
        finally { db.Entry(c).State = EntityState.Detached; db.Entry(row).State = EntityState.Detached; db.Entry(e).State = EntityState.Detached; }
    }
    public async Task CloseAsync(Guid id, EngineeringClose command, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var (c, task) = await Lock(id, actor, token); var fingerprint = Fingerprint(new { action = "Close", command });
        if (await Replay(c, command.RequestKey, fingerprint, actor, token)) return;
        Open(c, task); Revision(c.Revision, command.ExpectedRevision);
        var rows = await db.EngineeringStructures.AsNoTracking().Where(r => r.ChecklistId == c.Id).OrderBy(r => r.Position).ToArrayAsync(token);
        if (rows.Length == 0 || rows.Any(r => !r.Completed)) throw new DocumentException(409, "EngineeringIncomplete");
        if (c.CurrentDocumentId != command.DocumentId) throw new DocumentException(409, "EngineeringActaRequired");
        var a = await db.EngineeringActas.AsNoTracking().SingleAsync(a => a.ChecklistId == c.Id && a.DocumentId == command.DocumentId, token);
        var d = await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == a.DocumentId && d.ProjectId == id && d.IsEngineeringDocument, token);
        await VerifiedBytes(d, a.Sha256, token);
        var now = timing.UtcNow;
        db.Attach(c); db.Attach(task);
        c.Complete(now, actor.Subject, JsonSerializer.Serialize(new { c.SubmissionId, Structures = rows.Select(r => new { Structure = Source(r), r.Revision, r.Completed, r.ChangedAt, r.ChangedBySubject }), DocumentId = d.Id, a.Version, a.Sha256, CompletedAt = now, CompletedBy = actor.Subject }));
        task.Complete(now);
        var e = new EngineeringEvent(c, command.RequestKey, fingerprint, "Complete", null, now, actor.Subject, actor.Email, actor.Name);
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        var added = new List<object> { e };
        try
        {
            var next = await EfOpeningRecordStore.CreateInitialAsync(db, c, roles, now, token);
            added.AddRange(next.Entities); db.AddRange(added);
            staged = await notifications.StageAsync([task, next.Task], now, token);
            await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token);
        }
        catch { staged?.RestoreTracking(); throw; }
        finally { foreach (var entity in added.Concat(new object[] { c, task })) db.Entry(entity).State = EntityState.Detached; }
    }
    internal static async Task<byte[]> ReadBounded(Stream input, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + read > MaxPdfBytes) throw new DocumentException(413, "EngineeringActaTooLarge"); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        return output.ToArray();
    }
    internal static void ValidatePdf(string name, byte[] bytes)
    {
        try { ProjectDocument.ValidateFileName(name); } catch (ArgumentException) { throw new DocumentException(400, "InvalidEngineeringPdf"); }
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || bytes.Length < 32 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new DocumentException(400, "InvalidEngineeringPdf");
        // Format screening only, not antivirus or a PDF execution sandbox. Downloads are attachments.
        var tail = Encoding.ASCII.GetString(bytes.AsSpan(Math.Max(0, bytes.Length - 2048)));
        var xref = Regex.Match(tail, @"startxref\s+(\d+)\s+%%EOF\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!Regex.IsMatch(Encoding.ASCII.GetString(bytes, 0, 8), @"^%PDF-[12]\.\d$", RegexOptions.CultureInvariant) ||
            !xref.Success || !int.TryParse(xref.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 8 || offset >= bytes.Length)
            throw new DocumentException(400, "InvalidEngineeringPdf");
        var target = Encoding.ASCII.GetString(bytes, offset, Math.Min(256, bytes.Length - offset));
        // PDF permits either a classic xref table or an indirect cross-reference stream object.
        if (!target.StartsWith("xref", StringComparison.Ordinal) && !Regex.IsMatch(target, @"^\d+\s+\d+\s+obj\b", RegexOptions.CultureInvariant))
            throw new DocumentException(400, "InvalidEngineeringPdf");
    }
    public async Task UploadAsync(Guid id, Guid key, long revision, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token)
    {
        // Authorize before consuming multipart content; repeat under locks before storing anything.
        var access = await ReadAsync(id, new(actor.Subject, SystemRole.Engineering), token);
        if (access == null) throw new DocumentException(403, "EngineeringAccessDenied");
        byte[]? bytes = null; string? name = null; Guid? replaces = null;
        await foreach (var upload in uploads.WithCancellation(token))
        {
            if (bytes != null) throw new DocumentException(400, "InvalidEngineeringPdf");
            bytes = await ReadBounded(upload.Content, token); name = upload.FileName; replaces = upload.ReplacesDocumentId; ValidatePdf(name, bytes);
        }
        if (bytes == null) throw new DocumentException(400, "InvalidEngineeringPdf");
        var hash = Hash(bytes); var fingerprint = Fingerprint(new { action = "Upload", revision, name, replaces, hash });
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var (c, task) = await Lock(id, actor, token);
        if (await Replay(c, key, fingerprint, actor, token)) return;
        Open(c, task); Revision(c.Revision, revision);
        if (c.CurrentDocumentId != replaces) throw new DocumentException(409, "EngineeringRevisionConflict");
        var old = replaces is { } previous ? await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.Id == previous && d.ProjectId == id && d.IsEngineeringDocument, token) : null;
        var now = timing.UtcNow;
        var d = new ProjectDocument(Guid.NewGuid(), id, name!, "application/pdf", bytes.Length, key, now, actor.Subject, actor.Email, actor.Name, isEngineeringDocument: true);
        var version = (await db.EngineeringActas.Where(a => a.ChecklistId == c.Id).MaxAsync(a => (int?)a.Version, token) ?? 0) + 1;
        var a = new EngineeringActa(c, d, version, hash);
        db.Attach(c); if (old != null) { db.Attach(old); old.ReplaceWith(d, now); }
        c.SetDocument(d.Id);
        var e = new EngineeringEvent(c, key, fingerprint, "UploadActa", null, now, actor.Subject, actor.Email, actor.Name);
        var commitAttempted = false;
        try
        {
            await files.SaveAsync(d.StorageKey, bytes, token);
            db.AddRange(d, a, e); await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); commitAttempted = true; await tx.CommitAsync(token);
        }
        catch
        {
            if (!commitAttempted) { await tx.RollbackAsync(CancellationToken.None); await files.DeleteAsync(d.StorageKey, CancellationToken.None); }
            throw;
        }
        finally
        {
            foreach (var entity in new object?[] { c, old, d, a, e }.OfType<object>()) db.Entry(entity).State = EntityState.Detached;
        }
    }
    private async Task<byte[]> VerifiedBytes(ProjectDocument document, string hash, CancellationToken token)
    {
        try
        {
            await using var input = await files.OpenReadAsync(document.StorageKey, token);
            var bytes = await ReadBounded(input, token);
            if (bytes.LongLength != document.SizeBytes || Hash(bytes) != hash) throw new DocumentException(409, "EngineeringActaIntegrity");
            return bytes;
        }
        catch (FileNotFoundException) { throw new DocumentException(409, "EngineeringActaUnavailable"); }
        catch (DirectoryNotFoundException) { throw new DocumentException(409, "EngineeringActaUnavailable"); }
    }
    public async Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, ProjectReadScope scope, CancellationToken token)
    {
        if (!await CanRead(id, scope, token)) throw new DocumentException(403, "EngineeringAccessDenied");
        var file = await (from a in db.EngineeringActas.AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on a.DocumentId equals d.Id
                          where a.ProjectId == id && d.ProjectId == id && d.Id == documentId && d.IsEngineeringDocument select new { a, d }).SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "DocumentNotFound");
        return new(new MemoryStream(await VerifiedBytes(file.d, file.a.Sha256, token), false), file.d.OriginalFileName);
    }
    public async Task<byte[]?> SketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token)
    {
        if (!await CanRead(id, scope, token)) return null;
        var row = await db.EngineeringStructures.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.StructureId == structureId, token);
        if (row == null) return null;
        var source = Source(row);
        if (source.SketchVersionId != versionId && source.Modification?.SketchVersionId != versionId) return null;
        var sketch = await db.MeasurementSketchVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId && v.ProjectId == id && v.StructureId == structureId, token);
        if (sketch == null) return null;
        var bytes = await files.ReadAsync(sketch.StorageKey, token);
        if (Hash(bytes) != sketch.WebpSha256) throw new DocumentException(409, "MeasurementSketchIntegrity");
        return bytes;
    }
    public async Task InitializeHistoricalAsync(Guid id, EngineeringHistorical command, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var tasks = await LockProject(id, token);
        if (string.IsNullOrWhiteSpace(engineering.Subject) || actor.Subject != engineering.Subject) throw new DocumentException(403, "EngineeringAccessDenied");
        var existing = await db.EngineeringChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token);
        if (existing != null) { if (existing.SubmissionId != command.SubmissionId) throw new DocumentException(409, "EngineeringSourceInvalid"); return; }
        var source = await db.MeasurementChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id && c.SubmissionId == command.SubmissionId, token);
        if (source?.SnapshotJson == null || !tasks.Any(t => t.Id == source.TaskId && t.Status == ProjectTaskStatus.Completed) ||
            !await db.MeasurementReports.AnyAsync(r => r.ProjectId == id && r.SubmissionId == command.SubmissionId, token)) throw new DocumentException(409, "EngineeringSourceInvalid");
        var report = await db.MeasurementReports.AsNoTracking().SingleAsync(r => r.ProjectId == id && r.SubmissionId == command.SubmissionId, token);
        using var expected = JsonDocument.Parse(report.SnapshotCanonical);
        using var actual = JsonDocument.Parse(source.SnapshotJson);
        if (Hash(report.Pdf) != report.PdfSha256 || Hash(Encoding.UTF8.GetBytes(report.SnapshotCanonical)) != report.SnapshotSha256 ||
            !JsonElement.DeepEquals(expected.RootElement, actual.RootElement)) throw new DocumentException(409, "MeasurementReportIntegrity");
        var created = Create(source, JsonSerializer.Deserialize<MeasurementSnapshot>(report.SnapshotCanonical)!, engineering.Subject, timing.UtcNow, actor, command.Reason.Trim());
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try { db.AddRange(created.Entities); staged = await notifications.StageAsync([created.Task], timing.UtcNow, token); await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token); }
        catch { staged?.RestoreTracking(); throw; }
        finally { foreach (var entity in created.Entities) db.Entry(entity).State = EntityState.Detached; }
    }
}
