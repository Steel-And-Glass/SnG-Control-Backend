using System.Data;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfDepositVerificationStore(SnGControlDbContext db, TaskTiming timing, EfProjectNotificationWriter notifications) : IDepositVerificationStore
{
    private static DateTime Microseconds(DateTime instant) => new(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
    public async Task<VerificationState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var project = await db.Projects.AsNoTracking().Where(scope.Filter(db.ProjectTasks)).SingleOrDefaultAsync(p => p.Id == id, token);
        if (project is null) return null;
        var tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == id).ToArrayAsync(token);
        var approved = await DepositVerificationWorkflow.ApprovedAsync(db, project, tasks, token);
        var handoff = await db.AccountingHandoffs.AsNoTracking().SingleOrDefaultAsync(h => h.ProjectId == id, token);
        var deposit = handoff is null ? null : await db.InitialDeposits.AsNoTracking().SingleOrDefaultAsync(d => d.AccountingHandoffId == handoff.Id, token);
        var reviews = await db.DepositVerifications.AsNoTracking().Where(r => r.ProjectId == id).OrderBy(r => r.SubmittedAt).ThenBy(r => r.Id).ToArrayAsync(token);
        var links = await db.DepositVerificationDocuments.AsNoTracking().Where(d => d.ProjectId == id).ToArrayAsync(token);
        var docs = await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == id && d.IsPaymentReceipt && d.RemovedAt == null).OrderBy(d => d.UploadedAt).ThenBy(d => d.Id).ToArrayAsync(token);
        var pending = reviews.Any(r => r.Status == DepositVerificationStatus.Pending);
        var canManage = approved is not null && scope.Role == SystemRole.CommercialAdvisor && approved.Presentation.AssignedToGoogleSubject == scope.Subject && deposit is null && !pending;
        var canReview = scope.Role == SystemRole.Accounting && pending && deposit is null && tasks.Any(t => t.Id == handoff?.RegisterInitialDepositTaskId && t.AssignedToGoogleSubject == scope.Subject && t.Status == ProjectTaskStatus.Pending);
        await tx.CommitAsync(token);
        return new(id, approved is not null || handoff is not null, canManage, canManage && docs.Any(d => d.IsCurrent), canReview,
            AccountingState.From(id, handoff, deposit), docs.Select(d => DocumentResponse.From(d, new HashSet<Guid>())).ToArray(),
            reviews.Select(r => new VerificationReview(r.Id, r.Status.ToString(), r.SubmittedAt, r.DecidedAt,
                r.DecidedAt is null ? null : new(r.DecidedByEmail, r.DecidedByName), r.Note,
                links.Where(l => l.DepositVerificationId == r.Id).Select(l => l.ProjectDocumentId).Order().ToArray())).ToArray());
    }
    public async Task<Guid> SubmitAsync(Guid id, VerificationSubmission submission, DecisionActor actor, string accountant, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "ProjectNotFound");
        if (project.CreatedByGoogleSubject != actor.Subject) throw new DocumentException(403, "VerificationAccessDenied");
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var approved = await DepositVerificationWorkflow.ApprovedAsync(db, project, tasks, token) ?? throw new DocumentException(409, "ApprovedQuotationRequired");
        if (approved.Presentation.AssignedToGoogleSubject != actor.Subject) throw new DocumentException(403, "VerificationAccessDenied");
        var replay = await db.DepositVerifications.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == id && r.RequestKey == submission.RequestKey, token);
        if (replay is not null)
        {
            var existing = await db.DepositVerificationDocuments.Where(d => d.DepositVerificationId == replay.Id).Select(d => d.ProjectDocumentId).ToArrayAsync(token);
            if (!existing.Order().SequenceEqual(submission.DocumentIds.Order())) throw new DocumentException(409, "VerificationRequestConflict");
            return replay.Id;
        }
        await DepositVerificationWorkflow.RequireUploadAsync(db, project, tasks, actor.Subject, token);
        var documents = await db.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == id && d.IsPaymentReceipt && d.RemovedAt == null && d.ReplacedAt == null && submission.DocumentIds.Contains(d.Id)).ToArrayAsync(token);
        if (documents.Length != submission.DocumentIds.Count || documents.Length == 0) throw new DocumentException(400, "InvalidVerificationDocuments");
        var now = Microseconds(timing.UtcNow);
        var h = await db.AccountingHandoffs.AsNoTracking().SingleOrDefaultAsync(h => h.ProjectId == id, token);
        var added = new List<object>();
        ProjectTask task;
        if (h is null)
        {
            if (tasks.Any(t => t.Type == ProjectTaskType.RegisterInitialDeposit)) throw new DocumentException(409, "InvalidWorkflowState");
            task = new(id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, accountant, now);
            h = new(id, approved.Decision.Id, approved.Quotation.Id, approved.Round, task.Id, now, actor.Subject, actor.Email, actor.Name);
            added.AddRange([task, h]);
        }
        else
        {
            task = tasks.SingleOrDefault(t => t.Id == h.RegisterInitialDepositTaskId) ?? throw new DocumentException(409, "InvalidWorkflowState");
            if (h.CommercialQuotationId != approved.Quotation.Id || task.Status != ProjectTaskStatus.Pending || task.Area != OperationalArea.Accounting || task.Type != ProjectTaskType.RegisterInitialDeposit || string.IsNullOrWhiteSpace(task.AssignedToGoogleSubject))
                throw new DocumentException(409, "InvalidWorkflowState");
        }
        var review = new DepositVerification(h, submission.RequestKey, now, actor.Subject);
        added.Add(review); added.AddRange(documents.Select(d => new DepositVerificationDocument(review, d)));
        added.Add(ProjectNotification.DepositReview(task, task.AssignedToGoogleSubject!, review.Id, NotificationKind.DepositVerificationRequested, now));
        var corrections = tasks.Where(t => t.Type == ProjectTaskType.CorrectDepositReceipts && t.Status != ProjectTaskStatus.Completed).ToArray();
        if (corrections.Length > 1 || corrections.Any(t => t.AssignedToGoogleSubject != actor.Subject || t.Status != ProjectTaskStatus.InProgress))
            throw new DocumentException(409, "InvalidWorkflowState");
        var correction = corrections.SingleOrDefault();
        if (correction is not null) db.Attach(correction);
        var beforeCorrection = correction is null ? null : db.Entry(correction).CurrentValues.Clone();
        EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            if (correction is not null) { correction.Complete(now); }
            db.AddRange(added); staged = await notifications.StageAsync(correction is null ? [task] : [task, correction], now, token);
            await db.SaveChangesAsync(token); await tx.CommitAsync(token); return review.Id;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None); staged?.RestoreTracking();
            foreach (var entity in added) db.Entry(entity).State = EntityState.Detached;
            if (correction is not null) { db.Entry(correction).CurrentValues.SetValues(beforeCorrection!); db.Entry(correction).State = EntityState.Unchanged; }
            throw;
        }
    }
    public async Task DecideAsync(Guid id, Guid reviewId, bool approve, DepositInput? input, string? note, DecisionActor actor, string installer, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "ProjectNotFound");
        var tasks = await db.ProjectTasks.FromSqlInterpolated($"SELECT * FROM \"ProjectTasks\" WHERE \"ProjectId\" = {id} FOR UPDATE").ToArrayAsync(token);
        var review = await db.DepositVerifications.FromSqlInterpolated($"SELECT * FROM \"DepositVerifications\" WHERE \"Id\" = {reviewId} AND \"ProjectId\" = {id} FOR UPDATE").SingleOrDefaultAsync(token)
            ?? throw new DocumentException(404, "VerificationNotFound");
        var h = await db.AccountingHandoffs.AsNoTracking().SingleAsync(h => h.Id == review.AccountingHandoffId && h.ProjectId == id, token);
        var task = tasks.SingleOrDefault(t => t.Id == h.RegisterInitialDepositTaskId) ?? throw new DocumentException(409, "InvalidWorkflowState");
        if (task.AssignedToGoogleSubject != actor.Subject) throw new DocumentException(403, "VerificationAccessDenied");
        if (review.Status != DepositVerificationStatus.Pending) throw new DocumentException(409, "VerificationAlreadyDecided");
        if (task.Type != ProjectTaskType.RegisterInitialDeposit || task.Area != OperationalArea.Accounting || task.Status != ProjectTaskStatus.Pending
            || await db.InitialDeposits.AnyAsync(d => d.ProjectId == id, token) || tasks.Any(t => t.Type == ProjectTaskType.TakeMeasurements))
            throw new DocumentException(409, "InvalidWorkflowState");
        var approved = await DepositVerificationWorkflow.ApprovedAsync(db, project, tasks, token);
        if (approved is null || approved.Quotation.Id != h.CommercialQuotationId) throw new DocumentException(409, "ApprovedQuotationRequired");
        if (approve && (input is null || string.IsNullOrWhiteSpace(installer))) throw new DocumentException(400, "InvalidDeposit");
        var now = Microseconds(timing.UtcNow);
        if (input?.ReceivedAt > now) throw new DocumentException(400, "InvalidDeposit");
        var beforeTask = db.Entry(task).CurrentValues.Clone(); var beforeReview = db.Entry(review).CurrentValues.Clone();
        var added = new List<object>(); EfProjectNotificationWriter.NotificationWriteSet? staged = null;
        try
        {
            review.Decide(approve, note, now, actor.Subject, actor.Email, actor.Name);
            if (approve)
            {
                var deposit = new InitialDeposit(h, input!.Amount, Microseconds(input.ReceivedAt), now, null, note, actor.Subject, actor.Email, actor.Name);
                task.StartUntracked(now); task.Complete(now);
                var installation = new ProjectTask(id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, installer, now);
                added.AddRange([deposit, installation]); db.AddRange(added);
                staged = await notifications.StageAsync([task, installation], now, token);
            }
            else
            {
                if (tasks.Any(t => t.Type == ProjectTaskType.CorrectDepositReceipts && t.Status != ProjectTaskStatus.Completed))
                    throw new DocumentException(409, "InvalidWorkflowState");
                var correction = new ProjectTask(id, OperationalArea.Commercial, ProjectTaskType.CorrectDepositReceipts,
                    approved.Presentation.AssignedToGoogleSubject!, now);
                correction.StartUntracked(now);
                review.AssignCorrectionTask(correction);
                added.Add(correction); db.Add(correction);
                staged = await notifications.StageAsync([correction], now, token);
            }
            var notification = ProjectNotification.DepositReview(task, approved.Presentation.AssignedToGoogleSubject!, review.Id,
                approve ? NotificationKind.DepositVerificationApproved : NotificationKind.DepositVerificationRejected, now);
            added.Add(notification); db.Add(notification);
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None); staged?.RestoreTracking();
            foreach (var entity in added) db.Entry(entity).State = EntityState.Detached;
            db.Entry(task).CurrentValues.SetValues(beforeTask); db.Entry(task).State = EntityState.Unchanged;
            db.Entry(review).CurrentValues.SetValues(beforeReview); db.Entry(review).State = EntityState.Unchanged;
            throw;
        }
    }
}
