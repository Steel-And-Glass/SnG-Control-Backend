using System.Linq.Expressions;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Access;

public sealed class ProjectReadAccess(ICurrentUser user, IActiveRoleContext active)
{
    public ProjectReadScope Scope => new(user.Subject, active.Role);
}

// The same predicate is used by SQL queries and already-loaded document/quotation states.
// No status restriction: completed assignments preserve historical access.
public sealed record ProjectReadScope(string Subject, SystemRole? Role)
{
    public bool AllClients => Role is SystemRole.CommercialAdvisor or SystemRole.Management;
    public Expression<Func<Project, bool>> Filter(IQueryable<ProjectTask> tasks) => Role switch
    {
        SystemRole.Management => project => true,
        SystemRole.CommercialAdvisor when !string.IsNullOrEmpty(Subject) => project => project.CreatedByGoogleSubject == Subject,
        SystemRole.Budgets when !string.IsNullOrEmpty(Subject) => project => tasks.Any(task => task.ProjectId == project.Id
            && task.Type == ProjectTaskType.PrepareQuotation && task.AssignedToGoogleSubject == Subject),
        SystemRole.CommercialHead when !string.IsNullOrEmpty(Subject) => project => tasks.Any(task => task.ProjectId == project.Id
            && task.Type == ProjectTaskType.ReviewQuotation && task.AssignedToGoogleSubject == Subject),
        SystemRole.Accounting when !string.IsNullOrEmpty(Subject) => project => tasks.Any(task => task.ProjectId == project.Id
            && task.Type == ProjectTaskType.RegisterInitialDeposit && task.Area == OperationalArea.Accounting && task.AssignedToGoogleSubject == Subject),
        SystemRole.Engineering when !string.IsNullOrEmpty(Subject) => project => tasks.Any(task => task.ProjectId == project.Id
            && (task.Type == ProjectTaskType.PrepareOpeningRecord || task.Type == ProjectTaskType.ReviewOpeningRecord || task.Type == ProjectTaskType.FileOpeningRecord || task.Type == ProjectTaskType.EngineeringPackage) && task.Area == OperationalArea.Engineering && task.AssignedToGoogleSubject == Subject),
        SystemRole.Installation when !string.IsNullOrEmpty(Subject) => project => tasks.Any(task => task.ProjectId == project.Id
            && task.Type == ProjectTaskType.TakeMeasurements && task.Area == OperationalArea.Installation && task.AssignedToGoogleSubject == Subject),
        _ => project => false
    };
    public bool CanRead(Project project, IEnumerable<ProjectTask> tasks) => Filter(tasks.AsQueryable()).Compile()(project);
}
