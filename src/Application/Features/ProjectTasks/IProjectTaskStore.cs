using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.ProjectTasks;

public interface IProjectTaskStore
{
    // True when inserted; false when a PrepareQuotation task already exists for the project.
    Task<bool> TryAddPrepareQuotationAsync(ProjectTask task, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectTask>> ListByAreaAsync(
        OperationalArea area, ProjectTaskStatus status, SnG.Control.Application.Access.TaskInboxScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
