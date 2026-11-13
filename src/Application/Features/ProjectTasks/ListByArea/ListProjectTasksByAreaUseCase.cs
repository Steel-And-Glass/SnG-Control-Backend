using SnG.Control.Application.Access;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.ProjectTasks.ListByArea;

public class ListProjectTasksByAreaUseCase(IProjectTaskStore tasks, TaskTiming timing, ProjectReadAccess access)
{
    public async Task<IReadOnlyList<ProjectTaskResponse>?> ExecuteAsync(
        OperationalArea area, ProjectTaskStatus status, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area), area, "Area must be a defined value.");
        }
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Status must be a defined value.");
        }

        var scope = new TaskInboxScope(access.Scope.Subject, access.Scope.Role);
        if (!scope.Allows(area)) return null;
        var entities = await tasks.ListByAreaAsync(area, status, scope, cancellationToken);
        return entities.Select(task => ProjectTaskResponse.FromEntity(task, timing)).ToArray();
    }
}
