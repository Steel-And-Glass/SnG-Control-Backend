using SnG.Control.Domain.Enums;
using SnG.Control.Application.Access;
using SnG.Control.Application.Scheduling;
using SnG.Control.Application.Features.Projects;

namespace SnG.Control.Application.Features.ProjectTasks.ListByProject;

public class ListProjectTasksByProjectUseCase(IProjectStore projects, IProjectTaskStore tasks, TaskTiming timing, ProjectReadAccess access)
{
    // Area roles see only the tasks of their own area inside a project they can read (their part and their own history).
    private static OperationalArea? AreaFor(SystemRole? role) => role switch
    {
        SystemRole.Budgets => OperationalArea.Budgets,
        SystemRole.Accounting => OperationalArea.Accounting,
        SystemRole.Installation => OperationalArea.Installation,
        SystemRole.Engineering => OperationalArea.Engineering,
        _ => null
    };

    public async Task<IReadOnlyList<ProjectTaskResponse>?> ExecuteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (await projects.GetReadableByIdAsync(projectId, access.Scope, cancellationToken) is null)
        {
            return null;
        }

        var entities = await tasks.ListByProjectAsync(projectId, cancellationToken);
        var area = AreaFor(access.Scope.Role);
        return entities.Where(task => (area is null || task.Area == area) && task.Type != ProjectTaskType.CorrectDepositReceipts
            || access.Scope.Role == SystemRole.Management
            || (access.Scope.Role == SystemRole.CommercialAdvisor && task.AssignedToGoogleSubject == access.Scope.Subject)).Select(task => ProjectTaskResponse.FromEntity(task, timing)).ToArray();
    }
}
