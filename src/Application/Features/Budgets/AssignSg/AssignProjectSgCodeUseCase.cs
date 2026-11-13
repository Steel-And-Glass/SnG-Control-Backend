using SnG.Control.Application.Scheduling;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.Projects;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Budgets.AssignSg;

public class AssignProjectSgCodeUseCase(IBudgetsWorkflowStore store, TaskTiming timing, ICurrentUser user)
{
    public async Task<AssignProjectSgCodeResult> ExecuteAsync(Guid projectId, string sgCode, CancellationToken cancellationToken)
    {
        var (project, task, validSelection) = await store.LoadAsync(projectId, cancellationToken);
        if (project is null) return new AssignProjectSgCodeResult.ProjectNotFound();
        if (!validSelection) return new AssignProjectSgCodeResult.InvalidWorkflowState();
        if (task is null) return new AssignProjectSgCodeResult.ProjectNotSentToBudgets();

        if (!string.Equals(task.AssignedToGoogleSubject, user.Subject, StringComparison.Ordinal))
            return new AssignProjectSgCodeResult.InvalidWorkflowState();

        var firstAssignment = project.SgCode is null;
        var validState = task.CompletedAt is null && (firstAssignment
            ? task.Status == ProjectTaskStatus.Pending && task.StartedAt is null && task.DeadlineAt is null && task.WasOverdue is null
            : task.Status == ProjectTaskStatus.InProgress && task.StartedAt is not null);
        if (!validState) return new AssignProjectSgCodeResult.InvalidWorkflowState();

        project.SetSgCode(sgCode);
        if (firstAssignment) timing.Start(task, timing.UtcNow);
        if (!await store.TrySaveAsync(project, task, cancellationToken))
        {
            return new AssignProjectSgCodeResult.SgCodeAlreadyInUse();
        }

        return new AssignProjectSgCodeResult.Success(new AssignProjectSgCodeResponse(
            ProjectResponse.FromEntity(project, user.Subject), ProjectTaskResponse.FromEntity(task, timing)));
    }
}
