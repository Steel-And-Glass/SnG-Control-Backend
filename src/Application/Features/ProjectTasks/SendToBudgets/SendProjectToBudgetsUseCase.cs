using SnG.Control.Application.Features.ProjectDocuments;

namespace SnG.Control.Application.Features.ProjectTasks.SendToBudgets;

public class SendProjectToBudgetsUseCase(IProjectDocumentStore documents)
{
    public Task<SendProjectToBudgetsResult> ExecuteAsync(Guid projectId, CancellationToken cancellationToken) =>
        documents.SendToBudgetsAsync(projectId, cancellationToken);
}
