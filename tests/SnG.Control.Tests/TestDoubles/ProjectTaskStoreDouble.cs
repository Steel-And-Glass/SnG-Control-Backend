using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.TestDoubles;

public class ProjectTaskStoreDouble : IProjectTaskStore
{
    public SnG.Control.Application.Scheduling.TaskTiming Timing { get; set; } = TestTiming.Value;
    public ProjectStoreDouble Projects { get; set; } = new();
    public List<ProjectNotification> Notifications { get; } = [];
    public List<ProjectTask> Items { get; } = [];
    public List<CancellationToken> ReceivedTokens { get; } = [];
    public int InsertAttempts { get; private set; }

    public Task<bool> TryAddPrepareQuotationAsync(ProjectTask task, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        InsertAttempts++;
        if (Items.Any(existing => existing.ProjectId == task.ProjectId && existing.Type == ProjectTaskType.PrepareQuotation))
        {
            return Task.FromResult(false);
        }
        Items.Add(task);
        Notifications.AddRange(new SnG.Control.Application.Features.Notifications.ProjectNotificationPlanner(Timing).Reconcile(task, [], Timing.UtcNow));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ProjectTask>> ListByAreaAsync(
        OperationalArea area, ProjectTaskStatus status, SnG.Control.Application.Access.TaskInboxScope scope, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProjectTask>>(
            Items.AsQueryable().Where(scope.Filter()).Where(task => task.Area == area && task.Status == status
                && Projects.Items.Any(p => p.Id == task.ProjectId && new SnG.Control.Application.Access.ProjectReadScope(scope.Subject, scope.Role).CanRead(p, Items))).ToArray());
    }

    public Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProjectTask>>(Items.Where(task => task.ProjectId == projectId).ToArray());
    }
}
