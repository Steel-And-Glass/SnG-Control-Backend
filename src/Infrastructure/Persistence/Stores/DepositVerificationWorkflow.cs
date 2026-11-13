using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;

internal sealed record ApprovedDepositContext(Project Project, CommercialQuotation Quotation, int Round, ClientQuotationDecision Decision, ProjectTask Presentation);
internal static class DepositVerificationWorkflow
{
    // Caller holds the project lock for writes. Reads use a repeatable-read snapshot.
    internal static async Task<ApprovedDepositContext?> ApprovedAsync(SnGControlDbContext db, Project project, IReadOnlyList<ProjectTask> tasks, CancellationToken token)
    {
        var quote = await db.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == project.Id, token);
        if (quote is null) return null;
        var round = await db.BudgetQuotationVersions.Where(v => v.Id == quote.BudgetQuotationVersionId).Select(v => v.VersionNumber).SingleAsync(token);
        var decision = await db.ClientQuotationDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.CommercialQuotationId == quote.Id, token);
        var presentations = tasks.Where(t => t.Type == ProjectTaskType.PresentQuotationToClient && t.QuotationRound == round).ToArray();
        if (decision?.Decision != ClientQuotationDecisionType.Approved || presentations.Length != 1 || tasks.Any(t => t.QuotationRound > round)) return null;
        var presentation = presentations[0];
        if (presentation.Status != ProjectTaskStatus.Completed || presentation.Area != OperationalArea.Commercial || decision.ProjectId != project.Id
            || decision.PresentQuotationTaskId != presentation.Id || presentation.AssignedToGoogleSubject != project.CreatedByGoogleSubject) return null;
        return new(project, quote, round, decision, presentation);
    }
    internal static async Task RequireUploadAsync(SnGControlDbContext db, Project project, IReadOnlyList<ProjectTask> tasks, string subject, CancellationToken token)
    {
        var current = await ApprovedAsync(db, project, tasks, token);
        if (current is null) throw new DocumentException(409, "ApprovedQuotationRequired");
        if (current.Presentation.AssignedToGoogleSubject != subject) throw new DocumentException(403, "VerificationAccessDenied");
        if (await db.InitialDeposits.AnyAsync(d => d.ProjectId == project.Id, token)
            || await db.DepositVerifications.AnyAsync(r => r.ProjectId == project.Id && r.Status == DepositVerificationStatus.Pending, token))
            throw new DocumentException(409, "VerificationDocumentsLocked");
    }
}
