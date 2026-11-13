using System.Reflection;
using SnG.Control.Application.Features.Budgets;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.TestDoubles;

public sealed class BudgetsWorkflowStoreDouble(ProjectStoreDouble projects, ProjectTaskStoreDouble tasks) : IBudgetsWorkflowStore
{
    public List<CancellationToken> ReceivedTokens { get; } = [];
    public bool RejectSave { get; set; }
    public int SaveCalls { get; private set; }

    public Task<(Project? Project, ProjectTask? PrepareQuotationTask, bool ValidState)> LoadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var project = projects.Items.SingleOrDefault(project => project.Id == projectId);
        var task = tasks.Items.SingleOrDefault(task => task.ProjectId == projectId && task.Type == ProjectTaskType.PrepareQuotation);
        return Task.FromResult((Copy(project), Copy(task), true));
    }

    public Task<bool> TrySaveAsync(Project project, ProjectTask task, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SaveCalls++;
        if (RejectSave || projects.Items.Any(existing => existing.Id != project.Id && existing.SgCode == project.SgCode))
        {
            return Task.FromResult(false);
        }
        projects.Items[projects.Items.FindIndex(existing => existing.Id == project.Id)] = Copy(project)!;
        tasks.Items[tasks.Items.FindIndex(existing => existing.Id == task.Id)] = Copy(task)!;
        return Task.FromResult(true);
    }

    // Test-only snapshots keep failed edits separate from simulated persisted records.
    private static T? Copy<T>(T? entity) where T : class => entity is null ? null :
        (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(entity, null)!;
}
