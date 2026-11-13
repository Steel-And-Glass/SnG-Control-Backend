using SnG.Control.Application.Features.Measurements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Features.ProjectTasks.SendToBudgets;
using SnG.Control.Application.Files;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public class EfProjectDocumentStore(SnGControlDbContext context, IStreamingFileStorage storage,
    ProjectDocumentAccess access, TaskTiming timing) : IProjectDocumentStore, IReceiptDocumentStore, IMeasurementDocumentStore
{
    public virtual async Task<ProjectDocumentState?> LoadAsync(Guid projectId, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, token);
        var state = project is null ? null : await LoadDetailsAsync(project, token);
        await transaction.CommitAsync(token);
        return state;
    }
    private async Task<ProjectDocumentState> LoadDetailsAsync(Project project, CancellationToken token) => new(project,
        await context.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == project.Id).OrderBy(d => d.UploadedAt).ThenBy(d => d.Id).ToListAsync(token),
        await context.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.PrepareQuotation).OrderByDescending(t => t.QuotationRound).FirstOrDefaultAsync(token),
        await context.ProjectDocumentHandoffs.AsNoTracking().SingleOrDefaultAsync(h => h.ProjectId == project.Id, token),
        await context.ProjectDocumentHandoffItems.AsNoTracking().Where(i => i.ProjectId == project.Id).ToListAsync(token),
        await context.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == project.Id && t.Type == ProjectTaskType.ReviewQuotation).OrderByDescending(t => t.QuotationRound).FirstOrDefaultAsync(token),
        await context.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == project.Id).ToArrayAsync(token));

    protected virtual Task<IDbContextTransaction> BeginAsync(CancellationToken token) => context.Database.BeginTransactionAsync(token);
    protected virtual async Task<ProjectDocumentState?> LoadLockedAsync(Guid id, CancellationToken token)
    {
        // Every document writer locks Project before reading document/task state.
        var project = await context.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(token);
        return project is null ? null : await LoadDetailsAsync(project, token);
    }

    public Task<DocumentBatchResponse> UploadAsync(Guid projectId, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token) => UploadCoreAsync(projectId, uploads, false, false, token);
    public Task<DocumentBatchResponse> UploadReceiptsAsync(Guid projectId, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token) => UploadCoreAsync(projectId, uploads, true, false, token);
    public async Task RequireReceiptUploadAccessAsync(Guid id, CancellationToken token)
    {
        var state = await LoadAsync(id, token) ?? throw new DocumentException(404, "ProjectNotFound");
        access.RequireModify(state.Project);
        await DepositVerificationWorkflow.RequireUploadAsync(context, state.Project, state.HistoricalTasks ?? [], access.Current.Subject, token);
    }
    public Task<DocumentBatchResponse> UploadMeasurementsAsync(Guid id, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token, Guid? requestKey = null) => UploadCoreAsync(id, uploads, false, true, token, requestKey);
    public async Task RequireMeasurementUploadAccessAsync(Guid id, CancellationToken token)
    {
        access.RequireInstallation();
        var state = await LoadAsync(id, token) ?? throw new DocumentException(404, "ProjectNotFound");
        await EfMeasurementStore.RequireUploadAsync(context, id, state.HistoricalTasks ?? [], access.Current.Subject, token);
    }
    private async Task<DocumentBatchResponse> UploadCoreAsync(Guid projectId, IAsyncEnumerable<DocumentUpload> uploads, bool receipt, bool measurement, CancellationToken token, Guid? requestKey = null)
    {
        await using var transaction = await BeginAsync(token);
        var state = await LoadLockedAsync(projectId, token) ?? throw new DocumentException(404, "ProjectNotFound");
        if (measurement) { access.RequireInstallation(); await EfMeasurementStore.RequireUploadAsync(context, projectId, state.HistoricalTasks ?? [], access.Current.Subject, token); }
        else access.RequireModify(state.Project);
        if (receipt) await DepositVerificationWorkflow.RequireUploadAsync(context, state.Project, state.HistoricalTasks ?? [], access.Current.Subject, token);
        var batchId = measurement ? requestKey ?? Guid.NewGuid() : Guid.NewGuid();
        if (batchId == Guid.Empty) throw new DocumentException(400, "InvalidDocumentBatch");
        if (measurement)
        {
            // The first successful batch owns this key. Retrying it cannot create or replace files again.
            var replay = state.Documents.Where(d => d.IsMeasurementDocument && d.UploadBatchId == batchId).ToArray();
            if (replay.Length > 0)
            {
                if (replay.Any(d => d.UploadedByGoogleSubject != access.Current.Subject)) throw new DocumentException(409, "MeasurementRequestConflict");
                return new(batchId, replay.Select(d => DocumentResponse.From(d, new HashSet<Guid>())).ToArray());
            }
        }
        var added = new List<ProjectDocument>();
        var entities = new List<object>();
        var previous = new List<(ProjectDocument Document, PropertyValues Values)>();
        var keys = new List<string>();
        var replacements = new HashSet<Guid>();
        var parts = new HashSet<string>(StringComparer.Ordinal);
        var sizes = new DocumentBatchSize();
        var currentCount = state.Documents.Count(d => d.IsCurrent && d.IsPaymentReceipt == receipt && d.IsMeasurementDocument == measurement && !d.IsEngineeringDocument);
        var commitStarted = false;
        try
        {
            await foreach (var upload in uploads.WithCancellation(token))
            {
                if (added.Count >= ProjectDocument.MaxCurrentDocuments || !parts.Add(upload.PartName)) throw new DocumentException(400, "InvalidDocumentBatch");
                try { ProjectDocument.ValidateFileName(upload.FileName); }
                catch (ArgumentException) { throw new DocumentException(400, "InvalidDocumentBatch"); }
                ProjectDocument? old = null;
                if (upload.ReplacesDocumentId is Guid replaces)
                {
                    old = state.Documents.SingleOrDefault(d => d.Id == replaces && d.IsCurrent && d.IsPaymentReceipt == receipt && d.IsMeasurementDocument == measurement && !d.IsEngineeringDocument);
                    if (old is null || !replacements.Add(replaces)) throw new DocumentException(409, "InvalidReplacement");
                }
                else if (++currentCount > ProjectDocument.MaxCurrentDocuments) throw new DocumentException(409, "DocumentLimitExceeded");
                var id = Guid.NewGuid(); var key = ProjectDocument.Key(projectId, id);
                keys.Add(key); // Owned random key, including a failure after storage rename.
                using var content = new DocumentSizeStream(upload.Content, sizes);
                await storage.SaveFromStreamAsync(key, content, token);
                if (content.BytesRead == 0) throw new DocumentException(400, "InvalidDocumentBatch");
                var now = timing.UtcNow; var actor = access.Current;
                var document = new ProjectDocument(id, projectId, upload.FileName, upload.ContentType, content.BytesRead,
                    batchId, now, actor.Subject, actor.Email, actor.Name, receipt, measurement);
                added.Add(document); entities.Add(document); context.Add(document);
                if (old is not null)
                {
                    context.Attach(old); previous.Add((old, context.Entry(old).CurrentValues.Clone()));
                    old.ReplaceWith(document, now);
                }
            }
            if (added.Count == 0) throw new DocumentException(400, "InvalidDocumentBatch");
            if (!receipt && !measurement && state.PrepareQuotation is { Status: not ProjectTaskStatus.Completed } task)
            {
                var notification = ProjectNotification.DocumentsUpdated(task, task.AssignedToGoogleSubject ?? access.BudgetsSubject,
                    batchId, added.Count - replacements.Count, replacements.Count, timing.UtcNow);
                entities.Add(notification); context.Add(notification);
            }
            await context.SaveChangesAsync(token);
            commitStarted = true;
            await transaction.CommitAsync(token);
            return new(batchId, added.Select(d => DocumentResponse.From(d, new HashSet<Guid>())).ToArray());
        }
        catch (Exception failure)
        {
            var failures = new List<Exception>(); var rolledBack = false;
            try { await transaction.RollbackAsync(CancellationToken.None); rolledBack = true; }
            catch (Exception e) { failures.Add(e); }
            // An unacknowledged COMMIT is not proof of rollback. Preserve files when outcome is unknown.
            if (!commitStarted || rolledBack)
                foreach (var key in keys.AsEnumerable().Reverse())
                    try { await storage.DeleteAsync(key, CancellationToken.None); }
                    catch (Exception e) { failures.Add(e); }
            foreach (var entity in entities) context.Entry(entity).State = EntityState.Detached;
            foreach (var (document, values) in previous)
            {
                context.Entry(document).CurrentValues.SetValues(values);
                context.Entry(document).State = EntityState.Detached;
            }
            if (failures.Count > 0) throw new AggregateException("Document batch compensation requires inspection.", new[] { failure }.Concat(failures));
            throw;
        }
    }

    public async Task RemoveAsync(Guid projectId, Guid documentId, CancellationToken token)
    {
        await using var transaction = await BeginAsync(token);
        var state = await LoadLockedAsync(projectId, token) ?? throw new DocumentException(404, "ProjectNotFound");
        access.RequireModify(state.Project);
        if (state.PrepareQuotation is not null || state.Handoff is not null) throw new DocumentException(409, "DocumentRemovalLocked");
        var document = state.Documents.SingleOrDefault(d => d.Id == documentId && d.RemovedAt is null && !d.IsPaymentReceipt && !d.IsMeasurementDocument && !d.IsEngineeringDocument)
            ?? throw new DocumentException(404, "DocumentNotFound");
        if (!document.IsCurrent) throw new DocumentException(409, "InvalidReplacement");
        // Logical removal keeps the referenced bytes, avoiding a DB/filesystem deletion window.
        // All public queries/downloads exclude removed documents.
        context.Attach(document); var before = context.Entry(document).CurrentValues.Clone();
        try
        {
            document.Remove(timing.UtcNow);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            context.Entry(document).CurrentValues.SetValues(before);
            context.Entry(document).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<SendProjectToBudgetsResult> SendToBudgetsAsync(Guid projectId, CancellationToken token)
    {
        await using var transaction = await BeginAsync(token);
        var state = await LoadLockedAsync(projectId, token);
        if (state is null) return new SendProjectToBudgetsResult.ProjectNotFound();
        if (!access.CanModify(state.Project)) return new SendProjectToBudgetsResult.Forbidden();
        if (state.PrepareQuotation is not null || state.Handoff is not null) return new SendProjectToBudgetsResult.AlreadySent();
        var documents = state.Documents.Where(d => d.IsCurrent && !d.IsPaymentReceipt && !d.IsMeasurementDocument && !d.IsEngineeringDocument).ToArray();
        if (documents.Length == 0) return new SendProjectToBudgetsResult.DocumentsRequired();
        var now = timing.UtcNow; var actor = access.Current;
        var handoff = new ProjectDocumentHandoff(projectId, now, actor.Subject, actor.Email, actor.Name);
        var task = new ProjectTask(projectId, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, access.BudgetsSubject, now);
        var notification = new ProjectNotification(task, NotificationKind.TaskAssigned, "Nueva cotización asignada",
            "Nueva cotización asignada. Consulte el proyecto para ver el detalle.", now, now);
        var entities = new object[] { handoff, task, notification }.Concat(documents.Select(d => new ProjectDocumentHandoffItem(handoff, d))).ToArray();
        try
        {
            context.AddRange(entities);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new SendProjectToBudgetsResult.Created(ProjectTaskResponse.FromEntity(task, timing));
        }
        catch
        {
            foreach (var entity in entities) context.Entry(entity).State = EntityState.Detached;
            throw;
        }
    }
}
