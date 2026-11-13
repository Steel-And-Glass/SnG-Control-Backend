using SnG.Control.Domain.Entities;

namespace SnG.Control.Application.Features.Budgets;

public interface IBudgetsWorkflowStore
{
    // Loads the entities for one assignment, serializing assignments to the same project.
    Task<(Project? Project, ProjectTask? PrepareQuotationTask, bool ValidState)> LoadAsync(Guid projectId, CancellationToken cancellationToken);
    // Saves both entities atomically; false means the SG belongs to another project.
    Task<bool> TrySaveAsync(Project project, ProjectTask task, CancellationToken cancellationToken);
}
