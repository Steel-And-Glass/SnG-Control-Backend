using SnG.Control.Application.Features.Projects;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.ProjectTasks;

namespace SnG.Control.Application.Features.Budgets.PreviewQuotation;

public class PreviewBudgetQuotationUseCase(IProjectStore projects, IProjectTaskStore tasks, IBudgetQuotationParser parser, ICurrentUser user)
{
    public async Task<PreviewBudgetQuotationResult> ExecuteAsync(Guid projectId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null) return new PreviewBudgetQuotationResult.ProjectNotFound();
        var task = BudgetQuotationWorkflow.ReadyTask(project, await tasks.ListByProjectAsync(projectId, cancellationToken));
        if (task is null || !string.Equals(task.AssignedToGoogleSubject, user.Subject, StringComparison.Ordinal))
            return new PreviewBudgetQuotationResult.InvalidWorkflowState();

        var result = await parser.ParseAsync(stream, fileName, cancellationToken);
        if (result is BudgetQuotationParseResult.Invalid invalid)
            return new PreviewBudgetQuotationResult.InvalidWorkbook(invalid.Error);
        var parsed = (BudgetQuotationParseResult.Success)result;
        if (!string.Equals(parsed.Document.SgCode, project.SgCode, StringComparison.Ordinal))
            return new PreviewBudgetQuotationResult.SgCodeMismatch();
        return new PreviewBudgetQuotationResult.Success(new BudgetQuotationPreviewResponse(
            project.Id, project.SgCode!, parsed.Document, parsed.Items.Count,
            parsed.Items.Sum(item => (long)item.Quantity), parsed.Items, parsed.Warnings, task.QuotationRound!.Value, parsed.Economics));
    }
}
