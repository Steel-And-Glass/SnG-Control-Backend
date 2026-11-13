using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Budgets;

public static class BudgetQuotationWorkflow
{
    public static int? CurrentRound(IEnumerable<ProjectTask> tasks, ProjectTaskType type)
    {
        var candidates = tasks.Where(t => t.Type == type).ToArray();
        var active = candidates.Where(t => t.Status != ProjectTaskStatus.Completed).ToArray();
        return active.Length > 1 ? null : active.Length == 1 ? active[0].QuotationRound
            : candidates.Select(t => t.QuotationRound).DefaultIfEmpty().Max();
    }
    public static ProjectTask? ReadyTask(Project project, IEnumerable<ProjectTask> tasks)
    {
        var quotations = tasks.Where(task => task.Type == ProjectTaskType.PrepareQuotation && task.Status != ProjectTaskStatus.Completed).ToArray();
        if (project.SgCode is null || quotations.Length != 1) return null;
        var task = quotations[0];
        return task.ProjectId == project.Id && task.QuotationRound > 0 && task.Area == OperationalArea.Budgets && task.Status == ProjectTaskStatus.InProgress
            && task.StartedAt is not null && task.CompletedAt is null ? task : null;
    }
}
