using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Measurements;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed partial class EfMeasurementStore
{
    // DocumentId identifies the immutable file version. Scope membership is relational, not inferred.
    private sealed record DocumentEvidence(string FileName, string Sha256, DateTime AssociatedAt, string AssociatedBySubject);
    internal Task<MeasurementDelivery[]> ReadDeliveriesAsync(Guid id, CancellationToken token) => DeliveriesAsync(id, token);
    private async Task<MeasurementDelivery[]> DeliveriesAsync(Guid id, CancellationToken token)
    {
        var submissions = await db.Set<MeasurementSubmission>().AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Number).ToArrayAsync(token);
        var members = await db.Set<MeasurementSubmissionMember>().AsNoTracking().Where(m => m.ProjectId == id && m.EvidenceVersion == 1).OrderBy(m => m.Position).ToArrayAsync(token);
        var scopes = await db.Set<SubmissionDocumentScope>().AsNoTracking().Where(d => d.ProjectId == id).ToArrayAsync(token);
        var coverage = await db.Set<SubmissionDocumentMember>().AsNoTracking().Where(d => d.ProjectId == id && d.EvidenceVersion == 1).ToArrayAsync(token);
        return submissions.Select(s => new MeasurementDelivery(s.Id, s.Number, s.SubmittedAt, s.SubmittedBySubject, s.EvidenceStatus, s.ScopeIssue, s.ReportAvailability,
            members.Where(m => m.SubmissionId == s.Id).Select(m => m.StructureId).ToArray(), scopes.Where(d => d.SubmissionId == s.Id).OrderBy(d => d.DocumentId).Select(d => {
                var evidence = s.IsLegacy ? null : JsonSerializer.Deserialize<DocumentEvidence>(d.Declaration);
                return new MeasurementDeliveryDocument(d.DocumentId, evidence?.FileName, evidence?.Sha256,
                    coverage.Where(m => m.SubmissionId == s.Id && m.DocumentId == d.DocumentId).Select(m => m.StructureId).Order().ToArray(), evidence?.AssociatedAt, evidence?.AssociatedBySubject);
            }).ToArray())).ToArray();
    }
    public async Task<MeasurementDelivery> SubmitAsync(Guid id, MeasurementSubmissionCommand command, DecisionActor actor, CancellationToken token)
    {
        command.Validate();
        var normalized = command with { StructureIds = command.StructureIds.Order().ToArray(), Documents = command.Documents.OrderBy(d => d.DocumentId).Select(d => d with { StructureIds = d.StructureIds.Order().ToArray() }).ToArray() };
        var fingerprint = IncrementalEvidence.Hash(JsonSerializer.Serialize(normalized));
        // All ordinary writes and document replacements use the same project-first lock order.
        // Rendering under that lock deliberately serializes one project's deliveries, including disjoint selections.
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var task = await LockAsync(id, actor.Subject, token);
        var checklist = await db.MeasurementChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id, token);
        if (checklist?.IsIncremental != true || task.Status != ProjectTaskStatus.InProgress) throw new DocumentException(409, "MeasurementsLocked");
        var replay = await db.Set<MeasurementSubmission>().AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == id && s.RequestKey == command.RequestKey, token);
        if (replay != null)
        {
            if (replay.Fingerprint != fingerprint || replay.SubmittedBySubject != actor.Subject) throw new DocumentException(409, "MeasurementRequestConflict");
            return (await DeliveriesAsync(id, token)).Single(s => s.Id == replay.Id);
        }
        if (checklist.Revision != command.ExpectedRevision) throw new DocumentException(409, "MeasurementRevisionConflict");
        if (string.IsNullOrWhiteSpace(engineering?.Subject)) throw new DocumentException(503, "EngineeringAssigneeNotConfigured");
        var rows = await OrderedStructures(db.MeasurementStructures.Where(s => s.ChecklistId == checklist.Id && command.StructureIds.Contains(s.Id))).ToArrayAsync(token);
        if (rows.Length != command.StructureIds.Length) throw new DocumentException(404, "MeasurementStructureNotFound");
        if (rows.Any(s => s.RetiredAt != null || s.MeasuredAt == null || !s.IsTechnicallyComplete)) throw new DocumentException(409, "MeasurementsIncomplete");
        if (await db.Set<MeasurementSubmissionMember>().AnyAsync(s => s.ProjectId == id && s.EvidenceVersion == 1 && command.StructureIds.Contains(s.StructureId), token)) throw new DocumentException(409, "MeasurementsLocked");
        var documentIds = command.Documents.Select(d => d.DocumentId).ToArray();
        var documents = await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == id && documentIds.Contains(d.Id) && d.IsMeasurementDocument && d.RemovedAt == null && d.ReplacedAt == null).ToArrayAsync(token);
        if (documents.Length != documentIds.Length) throw new DocumentException(409, "MeasurementDocumentsChanged");
        var at = timing.UtcNow;
        var number = checked((await db.Set<MeasurementSubmission>().Where(s => s.ProjectId == id).MaxAsync(s => (int?)s.Number, token) ?? 0) + 1);
        var submissionId = Guid.NewGuid();
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == id, token);
        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == project.ClientId, token);
        var views = await StructureViewsAsync(rows, token);
        var header = new MeasurementReportHeader(submissionId, 1, project.Name, project.SgCode, client.Name, at, actor.Name ?? "Installation", 0, number);
        var snapshot = new MeasurementSnapshot(checklist.CommercialQuotationId, checklist.QuotationRound, MeasurementSummary.From(rows), views, documentIds.Order().ToArray(), header);
        var sketches = new Dictionary<Guid, byte[]>();
        foreach (var row in views)
            foreach (var version in new[] { row.SketchVersionId, row.Modification?.SketchVersionId }.OfType<Guid>().Distinct())
                sketches.Add(version, await ReadSketchImageAsync(id, row.Id, version, new(actor.Subject, SystemRole.Installation), token) ?? throw new DocumentException(409, "MeasurementReportUnavailable"));
        byte[] pdf;
        try { pdf = new InstallationPdfRenderer().Render(new(snapshot, sketches, await ReadQuotationImagesAsync(id, snapshot, token)), token); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { throw new DocumentException(409, "MeasurementReportUnavailable"); }
        var canonical = JsonSerializer.Serialize(snapshot); var snapshotHash = IncrementalEvidence.Hash(canonical);
        var entities = new List<object> { new MeasurementSubmission(submissionId, id, checklist.Id, number, at, actor.Subject, IncrementalEvidence.Complete, null, "PRESENT", canonical, snapshotHash, false, command.RequestKey, fingerprint),
            new SubmissionReport(submissionId, id, 1, null, pdf, Hash(pdf), snapshotHash, canonical) };
        entities.AddRange(views.Select((v, i) => { var json = JsonSerializer.Serialize(v); return new MeasurementSubmissionMember(submissionId, id, v.Id, 1, null, i, json, IncrementalEvidence.Hash(json)); }));
        foreach (var doc in documents)
        {
            byte[] bytes;
            try { bytes = await files.ReadAsync(doc.StorageKey, token); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw new DocumentException(409, "MeasurementDocumentsChanged"); }
            if (bytes.LongLength != doc.SizeBytes) throw new DocumentException(409, "MeasurementDocumentsChanged");
            entities.Add(new SubmissionDocumentScope(submissionId, id, doc.Id, JsonSerializer.Serialize(new DocumentEvidence(doc.OriginalFileName, Hash(bytes), at, actor.Subject))));
            entities.AddRange(command.Documents.Single(d => d.DocumentId == doc.Id).StructureIds.Select(s => new SubmissionDocumentMember(submissionId, id, doc.Id, s, 1)));
        }
        var engineeringTask = await db.ProjectTasks.AsNoTracking().SingleOrDefaultAsync(t => t.ProjectId == id && t.Type == ProjectTaskType.PrepareOpeningRecord, token);
        var newTask = engineeringTask == null;
        if (newTask)
        {
            if (await db.Set<EngineeringWorkspace>().AnyAsync(w => w.ProjectId == id, token)) throw new DocumentException(409, "EngineeringSourceInvalid");
            engineeringTask = new ProjectTask(id, OperationalArea.Engineering, ProjectTaskType.PrepareOpeningRecord, engineering!.Subject, at);
            engineeringTask.StartUntracked(at); entities.Add(engineeringTask); entities.Add(new EngineeringWorkspace(id, at));
        }
        else if (engineeringTask!.Status != ProjectTaskStatus.InProgress || engineeringTask.AssignedToGoogleSubject != engineering!.Subject || await db.EngineeringChecklists.AnyAsync(c => c.ProjectId == id, token)) throw new DocumentException(409, "EngineeringLocked");
        EngineeringWorkspace? changedWorkspace = null;
        if (!newTask) {
            var workspace = await db.Set<EngineeringWorkspace>().AsNoTracking().SingleAsync(w=>w.ProjectId==id,token);
            db.Attach(workspace); workspace.Advance(); changedWorkspace = workspace;
        }
        entities.Add(new EngineeringReceipt(id, submissionId, at));
        var installAssignment = new WorkPackageAssignment(Guid.NewGuid(), id, submissionId, 4, actor.Subject, at, at, null);
        var engineerAssignment = new WorkPackageAssignment(Guid.NewGuid(), id, submissionId, 5, engineering!.Subject, at, null, null);
        entities.Add(installAssignment); entities.Add(engineerAssignment);
        entities.Add(new EngineeringPackage(engineerAssignment, true, actor.Subject));
        foreach (var row in rows)
        {
            entities.Add(new EngineeringEvidenceCheck(id, submissionId, row.Id, 1, false, 0, null, null, null));
            entities.Add(new WorkPackageMember(installAssignment.Id, id, submissionId, row.Id, 1));
            var member = new WorkPackageMember(engineerAssignment.Id, id, submissionId, row.Id, 1);
            entities.Add(member); entities.Add(new EngineeringAssignedStructure(member));
        }
        db.Attach(checklist); checklist.Advance();
        entities.Add(new MeasurementEvent(checklist, command.RequestKey, fingerprint, "SubmitTranche", null, at, actor.Subject, actor.Email, actor.Name));
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            db.AddRange(entities);
            if (newTask) staged = await notifications.StageAsync([engineeringTask!], at, token);
            await db.SaveChangesAsync(token);
            var result = (await DeliveriesAsync(id, token)).Single(s => s.Id == submissionId);
            await tx.CommitAsync(token); return result;
        }
        catch { staged?.RestoreTracking(); throw; }
        finally { foreach (var entity in entities.Append(checklist).Concat(changedWorkspace == null ? [] : new object[] { changedWorkspace })) db.Entry(entity).State = EntityState.Detached; }
    }
    private async Task<MeasurementReportFile?> ReadIncrementalReportAsync(Guid id, Guid submissionId, string projectName, CancellationToken token)
    {
        var report = await db.Set<SubmissionReport>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.SubmissionId == submissionId && r.Version == 1 && r.LegacyChecklistId == null, token);
        if (report == null) return null;
        var submission = await db.Set<MeasurementSubmission>().AsNoTracking().SingleAsync(s => s.Id == submissionId && s.ProjectId == id, token);
        if (report.Pdf == null || Hash(report.Pdf) != report.PdfSha256 || report.SnapshotCanonical != submission.SnapshotCanonical || IncrementalEvidence.Hash(report.SnapshotCanonical!) != report.SnapshotSha256)
            throw new DocumentException(409, "MeasurementReportIntegrity");
        var baseName = MeasurementReportName.ForProject(projectName);
        return new(report.Pdf, QuotationDownloadName.Sanitize($"{baseName[..^4]}-Tramo {submission.Number:00}.pdf"));
    }
}
