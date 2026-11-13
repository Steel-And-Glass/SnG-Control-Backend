using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfOpeningRecordStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications,
    IStreamingFileStorage files, IEngineeringAssigneeIdentity engineering, IRoleResolver roles) : IOpeningRecordStore
{
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static string Fingerprint(object value) => Hash(Encoding.UTF8.GetBytes(Json(value)));
    private static DocumentException Conflict(string code) => new(409, code);
    private static void Revision(long actual, long expected) { if (actual != expected) throw Conflict("OpeningRevisionConflict"); }
    private Task<bool> CanRead(Guid id, ProjectReadScope scope, CancellationToken token) => db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).AnyAsync(x => x.Id == id, token);

    internal static async Task<(ProjectTask Task, IReadOnlyList<object> Entities)> CreateInitialAsync(SnGControlDbContext db,
        EngineeringChecklist c, IRoleResolver? roles, DateTime now, CancellationToken token)
    {
        var owner = await db.Projects.Where(p => p.Id == c.ProjectId).Select(p => p.CreatedByGoogleSubject).SingleAsync(token);
        RequireAdvisor(owner, roles);
        if (await db.OpeningRecordRounds.AnyAsync(r => r.ProjectId == c.ProjectId, token)) throw Conflict("OpeningAlreadyInitialized");
        var task = NewTask(c.ProjectId, ProjectTaskType.PresentOpeningRecord, owner!, now);
        var round = new OpeningRecordRound(c, 1, task.Id, c.CurrentDocumentId!.Value, now);
        var ids = await db.EngineeringStructures.AsNoTracking().Where(s => s.ChecklistId == c.Id).OrderBy(s => s.Position).Select(s => s.StructureId).ToArrayAsync(token);
        if (ids.Length == 0) throw Conflict("OpeningSourceInvalid");
        return (task, new object[] { task, round }.Concat(ids.Select(id => new OpeningRecordStructure(round.Id, id))).ToArray());
    }
    private static void RequireAdvisor(string? owner, IRoleResolver? resolver)
    { if (string.IsNullOrWhiteSpace(owner) || resolver?.Resolve(owner).Contains(SystemRole.CommercialAdvisor) != true) throw new DocumentException(503, "OpeningAdvisorNotConfigured"); }
    private void RequireEngineer()
    { if (string.IsNullOrWhiteSpace(engineering.Subject) || !roles.Resolve(engineering.Subject).Contains(SystemRole.Engineering)) throw new DocumentException(503, "EngineeringAssigneeNotConfigured"); }
    private static ProjectTask NewTask(Guid id, ProjectTaskType type, string subject, DateTime now)
    {
        var task = new ProjectTask(id, type == ProjectTaskType.PresentOpeningRecord ? OperationalArea.Commercial : OperationalArea.Engineering, type, subject, now);
        if (type != ProjectTaskType.FileOpeningRecord) task.StartUntracked(now);
        return task;
    }
    private async Task<(Project Project, ProjectTask[] Tasks)> Lock(Guid id, CancellationToken token)
    {
        var project = await db.Projects.FromSqlInterpolated($""" SELECT * FROM "Projects" WHERE "Id" = {id} FOR UPDATE """).AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "ProjectNotFound");
        var tasks = await db.ProjectTasks.FromSqlInterpolated($""" SELECT * FROM "ProjectTasks" WHERE "ProjectId" = {id} FOR UPDATE """).AsNoTracking().ToArrayAsync(token);
        return (project, tasks);
    }
    private static ProjectTask Authorize(OpeningRecordRound round, Project project, ProjectTask[] tasks, DecisionActor actor, SystemRole role, bool review)
    {
        var task = tasks.SingleOrDefault(t => t.Id == (review ? round.ReviewTaskId : round.PresentationTaskId));
        if (role != (review ? SystemRole.Engineering : SystemRole.CommercialAdvisor) || task?.AssignedToGoogleSubject != actor.Subject ||
            (!review && project.CreatedByGoogleSubject != actor.Subject)) throw new DocumentException(403, "OpeningAccessDenied");
        return task;
    }
    private async Task<bool> Replay(OpeningRecordRound r, Guid key, string fingerprint, DecisionActor actor, CancellationToken token)
    {
        var prior = await db.OpeningRecordEvents.AsNoTracking().SingleOrDefaultAsync(e => e.RoundId == r.Id && e.RequestKey == key, token);
        if (prior == null) return false;
        if (prior.Fingerprint != fingerprint || prior.ActorSubject != actor.Subject) throw Conflict("OpeningRequestConflict");
        return true;
    }
    public async Task<OpeningState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        if (!await CanRead(id, scope, token)) return null;
        var rounds = await db.OpeningRecordRounds.AsNoTracking().Where(r => r.ProjectId == id).OrderBy(r => r.Number).ToArrayAsync(token);
        if (rounds.Length == 0) return null;
        var c = await db.EngineeringChecklists.AsNoTracking().SingleAsync(c => c.ProjectId == id, token);
        var structures = await db.EngineeringStructures.AsNoTracking().Where(s => s.ChecklistId == c.Id).OrderBy(s => s.Position).ToArrayAsync(token);
        var ids = rounds.Select(r => r.Id).ToArray();
        var rows = await db.OpeningRecordStructures.AsNoTracking().Where(s => ids.Contains(s.RoundId)).ToArrayAsync(token);
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id).ToDictionaryAsync(t => t.Id, token);
        var documents = await (from a in db.OpeningRecordDocuments.AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on a.DocumentId equals d.Id where a.ProjectId == id select new { a, d }).ToArrayAsync(token);
        var initial = await (from a in db.EngineeringActas.AsNoTracking() join d in db.ProjectDocuments.AsNoTracking() on a.DocumentId equals d.Id where d.Id == rounds[0].PresentedDocumentId select new { a, d }).SingleAsync(token);
        var history = await db.OpeningRecordEvents.AsNoTracking().Where(e => ids.Contains(e.RoundId)).OrderBy(e => e.At).ThenBy(e => e.Revision).ToArrayAsync(token);
        var now = timing.UtcNow;
        var rowLookup = rows.ToDictionary(s => (s.RoundId, s.StructureId));
        var result = rounds.Select(r =>
        {
            bool Assigned(Guid? taskId, SystemRole role) => scope.Role == role && taskId.HasValue && tasks[taskId.Value].AssignedToGoogleSubject == scope.Subject && tasks[taskId.Value].Status == ProjectTaskStatus.InProgress;
            var ordered = structures.Select(s => rowLookup[(r.Id, s.StructureId)]);
            return new OpeningRoundView(r.Id, r.Number, r.Status.ToString(), r.Revision, r.PresentationTaskId, r.ReviewTaskId, r.FilingTaskId,
                r.PresentedDocumentId, r.RevisedDocumentId, r.SignedDocumentId, r.ReceivedAt, r.PresentedAt, r.RespondedAt,
                r.PresentedAt is { } start ? Math.Max(0, ((r.RespondedAt ?? now) - start).Ticks / (decimal)TimeSpan.TicksPerSecond) : null,
                r.GeneralObservations, r.AffectedDeclared, r.Status == OpeningRoundStatus.Ready && Assigned(r.PresentationTaskId, SystemRole.CommercialAdvisor),
                r.Status == OpeningRoundStatus.Presented && Assigned(r.PresentationTaskId, SystemRole.CommercialAdvisor),
                r.Status == OpeningRoundStatus.ChangesRequested && Assigned(r.ReviewTaskId, SystemRole.Engineering),
                ordered.Select(s => new OpeningRow(s.StructureId, s.Approval.ToString(), s.ApprovalDocumentId, s.CarriedApproval, s.ChangeDescription, s.Affected, s.ReviewCompleted, s.Revision)).ToArray());
        }).ToArray();
        var docs = new[] { new OpeningDocumentView(rounds[0].Id, "Original", initial.a.Version, initial.a.Sha256, DocumentResponse.From(initial.d, new HashSet<Guid>()), []) }
            .Concat(documents.OrderBy(x => x.d.UploadedAt).ThenBy(x => x.a.Version).Select(x => new OpeningDocumentView(x.a.RoundId, x.a.Kind.ToString(), x.a.Version, x.a.Sha256, DocumentResponse.From(x.d, new HashSet<Guid>()), JsonSerializer.Deserialize<Guid[]>(x.a.AffectedIdsJson)!))).ToArray();
        await tx.CommitAsync(token);
        return new(id, c.SubmissionId, now, structures.Select(s => JsonSerializer.Deserialize<MeasurementStructureView>(s.SnapshotJson)!).ToArray(), result, docs,
            history.Select(e => new OpeningEventView(e.RoundId, e.Action, e.At, e.ActorName, e.Revision)).ToArray());
    }

    public async Task CommandAsync(Guid id, Guid roundId, OpeningCommand command, DecisionActor actor, SystemRole role, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var (project, tasks) = await Lock(id, token);
        var r = await db.OpeningRecordRounds.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.Id == roundId, token) ?? throw new DocumentException(404, "OpeningRoundNotFound");
        var review = command.Action is "DeclareAffected" or "ReviewCheck" or "CloseReview";
        var task = Authorize(r, project, tasks, actor, role, review);
        var fingerprint = Fingerprint(command);
        if (await Replay(r, command.RequestKey, fingerprint, actor, token)) return;
        if (task.Status != ProjectTaskStatus.InProgress) throw Conflict("OpeningRoundStateConflict");
        var rows = await db.OpeningRecordStructures.AsNoTracking().Where(s => s.RoundId == r.Id).OrderBy(s => s.StructureId).ToArrayAsync(token);
        var row = command.StructureId is { } sid ? rows.SingleOrDefault(x => x.StructureId == sid) : null;
        if (command.Action is "ConfirmStructure" or "ReviewCheck") { if (row == null || command.Value == null) throw new DocumentException(400, "InvalidOpeningCommand"); Revision(row.Revision, command.ExpectedRevision); }
        else Revision(r.Revision, command.ExpectedRevision);
        var owned = new List<object> { r, task }; owned.AddRange(rows); db.AttachRange(owned);
        var changedTasks = new List<ProjectTask> { task }; var now = timing.UtcNow;
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        var before = Json(rows.Select(s => new { s.StructureId, s.Approval, s.Affected, s.ReviewCompleted, s.Revision }));
        try
        {
            switch (command.Action)
            {
                case "Present":
                    await VerifyDocument(id, r.PresentedDocumentId, token); r.Present(now, actor.Subject); break;
                case "ConfirmStructure": row!.Confirm(r, command.Value!.Value); r.Advance(); break;
                case "RequestChanges":
                    r.Require(OpeningRoundStatus.Presented); RequireEngineer();
                    if (command.Changes is not { Count: > 0 } changes || changes.Select(x => x.StructureId).Distinct().Count() != changes.Count) throw new DocumentException(400, "InvalidOpeningCommand");
                    foreach (var change in changes)
                    {
                        var selected = rows.SingleOrDefault(x => x.StructureId == change.StructureId) ?? throw new DocumentException(400, "InvalidOpeningCommand");
                        selected.RequestChange(r, change.Description.Trim());
                    }
                    foreach (var other in rows.Where(s => s.Approval == OpeningApproval.AwaitingApproval)) other.Confirm(r, true);
                    var reviewTask = NewTask(id, ProjectTaskType.ReviewOpeningRecord, engineering.Subject, now);
                    r.RequestChanges(now, actor.Subject, reviewTask.Id, command.Observations?.Trim()); task.Complete(now);
                    Add(reviewTask); break;
                case "DeclareAffected":
                    r.Require(OpeningRoundStatus.ChangesRequested);
                    if (command.AffectedStructureIds is not { Count: > 0 } affected || affected.Distinct().Count() != affected.Count || affected.Any(x => rows.All(s => s.StructureId != x))) throw new DocumentException(400, "InvalidOpeningCommand");
                    foreach (var s in rows) s.DeclareAffected(r, affected.Contains(s.StructureId));
                    r.DeclareAffected(); break;
                case "ReviewCheck": row!.CheckReview(r, command.Value!.Value); r.Advance(); break;
                case "CloseReview":
                    r.Require(OpeningRoundStatus.ChangesRequested);
                    RequireAdvisor(project.CreatedByGoogleSubject, roles);
                    if (!r.AffectedDeclared || rows.Where(s => s.Affected).Any(s => !s.ReviewCompleted) || !rows.Any(s => s.Affected)) throw Conflict("OpeningReviewIncomplete");
                    if (r.RevisedDocumentId == null || command.DocumentId != r.RevisedDocumentId) throw Conflict("OpeningRevisedActaRequired");
                    var revised = await db.OpeningRecordDocuments.AsNoTracking().SingleAsync(d => d.RoundId == r.Id && d.DocumentId == r.RevisedDocumentId && d.Kind == OpeningDocumentKind.Revised, token);
                    if (!JsonSerializer.Deserialize<Guid[]>(revised.AffectedIdsJson)!.Order().SequenceEqual(rows.Where(s => s.Affected).Select(s => s.StructureId).Order())) throw Conflict("OpeningAffectedDeclarationChanged");
                    await VerifyDocument(id, revised.DocumentId, token);
                    r.CompleteReview(now, actor.Subject, Json(new { r.SubmissionId, r.PresentedDocumentId, RevisedDocumentId = revised.DocumentId, revised.Sha256, Structures = rows })); task.Complete(now);
                    // Release the unique active-round slot inside the same transaction before inserting its successor.
                    await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token);
                    var source = await db.EngineeringChecklists.AsNoTracking().SingleAsync(c => c.Id == r.ChecklistId, token);
                    var nextTask = NewTask(id, ProjectTaskType.PresentOpeningRecord, project.CreatedByGoogleSubject!, now);
                    var next = new OpeningRecordRound(source, checked(r.Number + 1), nextTask.Id, revised.DocumentId, now);
                    Add(nextTask); owned.Add(next); db.Add(next);
                    foreach (var s in rows) { var nextRow = new OpeningRecordStructure(next.Id, s.StructureId, s); owned.Add(nextRow); db.Add(nextRow); }
                    break;
                case "Accept":
                    r.Require(OpeningRoundStatus.Presented); RequireEngineer();
                    if (rows.Any(s => s.Approval != OpeningApproval.Confirmed)) throw Conflict("OpeningApprovalsIncomplete");
                    if (r.SignedDocumentId == null || command.DocumentId != r.SignedDocumentId || command.AttestSignedForPresentedVersion != true) throw Conflict("OpeningSignedActaRequired");
                    var signed = await db.OpeningRecordDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.RoundId == r.Id && d.DocumentId == r.SignedDocumentId && d.Kind == OpeningDocumentKind.Signed, token) ?? throw Conflict("OpeningSignedActaRequired");
                    await VerifyDocument(id, signed.DocumentId, token); await VerifyDocument(id, r.PresentedDocumentId, token);
                    var filing = NewTask(id, ProjectTaskType.FileOpeningRecord, engineering.Subject, now);
                    r.Accept(now, actor.Subject, filing.Id, Json(new { r.SubmissionId, RoundId = r.Id, r.Number, r.PresentedDocumentId, SignedDocumentId = signed.DocumentId, signed.Sha256, Structures = rows, AcceptedAt = now, AcceptedBy = actor.Subject, AttestedByAdvisor = true }));
                    task.Complete(now); Add(filing); break;
                default: throw new DocumentException(400, "InvalidOpeningCommand");
            }
            var e = new OpeningRecordEvent(r, command.RequestKey, fingerprint, command.Action, Json(new { TaskId = task.Id, Command = command, Before = JsonSerializer.Deserialize<JsonElement>(before), After = rows }), now, actor.Subject, actor.Name);
            owned.Add(e); db.Add(e); staged = await notifications.StageAsync(changedTasks, now, token);
            await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token);
        }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Opening", StringComparison.Ordinal)) { staged?.RestoreTracking(); throw Conflict(error.Message); }
        catch { staged?.RestoreTracking(); throw; }
        finally { foreach (var entity in owned) db.Entry(entity).State = EntityState.Detached; }
        void Add(ProjectTask added) { owned.Add(added); db.Add(added); changedTasks.Add(added); }
    }

    public async Task UploadAsync(Guid id, Guid roundId, Guid key, long revision, OpeningDocumentKind kind,
        IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, SystemRole role, CancellationToken token)
    {
        // Check assignment before reading a private multipart body, and again under project/task locks.
        if ((kind == OpeningDocumentKind.Signed ? role != SystemRole.CommercialAdvisor : role != SystemRole.Engineering) || !await CanRead(id, new(actor.Subject, role), token))
            throw new DocumentException(403, "OpeningAccessDenied");
        var assignment = await db.OpeningRecordRounds.AsNoTracking().Where(r => r.ProjectId == id && r.Id == roundId)
            .Select(r => kind == OpeningDocumentKind.Signed ? (Guid?)r.PresentationTaskId : r.ReviewTaskId).SingleOrDefaultAsync(token);
        if (!await db.ProjectTasks.AnyAsync(t => t.Id == assignment && t.AssignedToGoogleSubject == actor.Subject, token)) throw new DocumentException(403, "OpeningAccessDenied");
        byte[]? bytes = null; string? name = null; Guid? replaces = null;
        await foreach (var upload in uploads.WithCancellation(token))
        {
            if (bytes != null) throw new DocumentException(400, "InvalidEngineeringPdf");
            bytes = await EfEngineeringStore.ReadBounded(upload.Content, token); name = upload.FileName; replaces = upload.ReplacesDocumentId; EfEngineeringStore.ValidatePdf(name, bytes);
        }
        if (bytes == null) throw new DocumentException(400, "InvalidEngineeringPdf");
        var hash = Hash(bytes); var fingerprint = Fingerprint(new { Action = "Upload", kind, revision, name, replaces, hash });
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var (project, tasks) = await Lock(id, token);
        var r = await db.OpeningRecordRounds.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.Id == roundId, token) ?? throw new DocumentException(404, "OpeningRoundNotFound");
        var task = Authorize(r, project, tasks, actor, role, kind == OpeningDocumentKind.Revised);
        if (await Replay(r, key, fingerprint, actor, token)) return;
        if (task.Status != ProjectTaskStatus.InProgress || r.Status != (kind == OpeningDocumentKind.Signed ? OpeningRoundStatus.Presented : OpeningRoundStatus.ChangesRequested)) throw Conflict("OpeningRoundStateConflict");
        Revision(r.Revision, revision);
        if ((kind == OpeningDocumentKind.Signed ? r.SignedDocumentId : r.RevisedDocumentId) != replaces) throw Conflict("OpeningRevisionConflict");
        var affected = await db.OpeningRecordStructures.AsNoTracking().Where(s => s.RoundId == r.Id && s.Affected).OrderBy(s => s.StructureId).Select(s => s.StructureId).ToArrayAsync(token);
        if (kind == OpeningDocumentKind.Revised && (!r.AffectedDeclared || affected.Length == 0)) throw Conflict("OpeningAffectedStructuresRequired");
        var now = timing.UtcNow;
        var document = new ProjectDocument(Guid.NewGuid(), id, name!, "application/pdf", bytes.Length, key, now, actor.Subject, actor.Email, actor.Name, isEngineeringDocument: true);
        var version = (await db.OpeningRecordDocuments.Where(d => d.ProjectId == id && d.Kind == kind).MaxAsync(d => (int?)d.Version, token) ?? 0) + 1;
        if (kind == OpeningDocumentKind.Revised) version = Math.Max(version, (await db.EngineeringActas.Where(d => d.ProjectId == id).MaxAsync(d => (int?)d.Version, token) ?? 0) + 1);
        var attachment = new OpeningRecordDocument(r, document, kind, version, hash, Json(kind == OpeningDocumentKind.Revised ? affected : []));
        var old = replaces.HasValue ? await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.ProjectId == id && d.Id == replaces, token) : null;
        db.Attach(r); if (old != null) { db.Attach(old); old.ReplaceWith(document, now); }
        r.SetDocument(document.Id, kind);
        var e = new OpeningRecordEvent(r, key, fingerprint, "Upload" + kind, Json(new { TaskId = task.Id, DocumentId = document.Id, version, hash, AffectedStructureIds = affected, Replaces = replaces }), now, actor.Subject, actor.Name);
        var commitAttempted = false;
        try
        {
            await files.SaveAsync(document.StorageKey, bytes, token); db.AddRange(document, attachment, e);
            await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); commitAttempted = true; await tx.CommitAsync(token);
        }
        catch { if (!commitAttempted) { await tx.RollbackAsync(CancellationToken.None); await files.DeleteAsync(document.StorageKey, CancellationToken.None); } throw; }
        finally { foreach (var entity in new object?[] { r, old, document, attachment, e }.OfType<object>()) db.Entry(entity).State = EntityState.Detached; }
    }
    private async Task<(ProjectDocument Document, byte[] Bytes)> VerifyDocument(Guid id, Guid documentId, CancellationToken token)
    {
        var hash = await db.OpeningRecordDocuments.AsNoTracking().Where(d => d.ProjectId == id && d.DocumentId == documentId).Select(d => d.Sha256).SingleOrDefaultAsync(token)
            ?? await db.EngineeringActas.AsNoTracking().Where(d => d.ProjectId == id && d.DocumentId == documentId).Select(d => d.Sha256).SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "DocumentNotFound");
        var document = await db.ProjectDocuments.AsNoTracking().SingleAsync(d => d.ProjectId == id && d.Id == documentId && d.IsEngineeringDocument, token);
        try
        {
            await using var input = await files.OpenReadAsync(document.StorageKey, token);
            var bytes = await EfEngineeringStore.ReadBounded(input, token);
            if (bytes.LongLength != document.SizeBytes || Hash(bytes) != hash) throw Conflict("EngineeringActaIntegrity");
            return (document, bytes);
        }
        catch (FileNotFoundException) { throw Conflict("EngineeringActaUnavailable"); }
        catch (DirectoryNotFoundException) { throw Conflict("EngineeringActaUnavailable"); }
    }
    public async Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, ProjectReadScope scope, CancellationToken token)
    {
        if (!await CanRead(id, scope, token)) throw new DocumentException(403, "OpeningAccessDenied");
        var result = await VerifyDocument(id, documentId, token);
        return new(new MemoryStream(result.Bytes, false), result.Document.OriginalFileName);
    }
    public async Task InitializeHistoricalAsync(Guid id, Guid checklistId, string reason, DecisionActor actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token); var (_, tasks) = await Lock(id, token);
        var c = await db.EngineeringChecklists.AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == id && c.Id == checklistId, token) ?? throw Conflict("OpeningSourceInvalid");
        if (tasks.Single(t => t.Id == c.TaskId).AssignedToGoogleSubject != actor.Subject || engineering.Subject != actor.Subject) throw new DocumentException(403, "OpeningAccessDenied");
        if (c.CompletedAt == null || c.FinalSnapshotJson == null || c.CurrentDocumentId == null) throw Conflict("OpeningSourceInvalid");
        if (await db.OpeningRecordRounds.AnyAsync(r => r.ProjectId == id, token)) return;
        await VerifyDocument(id, c.CurrentDocumentId.Value, token);
        var next = await CreateInitialAsync(db, c, roles, timing.UtcNow, token);
        var round = next.Entities.OfType<OpeningRecordRound>().Single();
        var e = new OpeningRecordEvent(round, Guid.NewGuid(), "", "InitializeHistorical", Json(new { reason, checklistId }), timing.UtcNow, actor.Subject, actor.Name);
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try { db.AddRange(next.Entities); db.Add(e); staged = await notifications.StageAsync([next.Task], timing.UtcNow, token); await db.SaveChangesAsync(token); await IncrementalLegacyProjection.SyncAsync(db, id, token); await tx.CommitAsync(token); }
        catch { staged?.RestoreTracking(); throw; }
        finally { foreach (var entity in next.Entities.Append(e)) db.Entry(entity).State = EntityState.Detached; }
    }
}
