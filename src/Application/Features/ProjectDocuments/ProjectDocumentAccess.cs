using SnG.Control.Application.Access;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.ProjectDocuments;

public sealed class ProjectDocumentAccess(ICurrentUser current, IActiveRoleContext active, IBudgetsAssigneeIdentity budgets)
{
    public void RequireInstallation()
    {
        if (active.Role != SystemRole.Installation) throw new DocumentException(403, "MeasurementAccessDenied");
    }
    public ICurrentUser Current => current;
    public string BudgetsSubject => budgets.Subject;
    public bool CanModify(Project project) => active.Role == SystemRole.CommercialAdvisor && string.Equals(current.Subject, project.CreatedByGoogleSubject, StringComparison.Ordinal);
    public bool CanSendToBudgets(Project project) => active.Role == SystemRole.CommercialAdvisor && CanModify(project);
    public bool CanRead(ProjectDocumentState state) => new ProjectReadScope(current.Subject, active.Role).CanRead(state.Project,
        state.HistoricalTasks ?? new[] { state.PrepareQuotation, state.ReviewQuotation }.OfType<ProjectTask>().ToArray());
    public void RequireModify(Project project)
    {
        if (!CanModify(project)) throw new DocumentException(403, "DocumentAccessDenied");
    }
}
