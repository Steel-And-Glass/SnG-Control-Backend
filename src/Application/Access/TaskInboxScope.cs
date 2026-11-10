using System.Linq.Expressions;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Access;

// Personal work is narrower than readable project history.
public sealed record TaskInboxScope(string Subject, SystemRole? Role)
{
    private (OperationalArea Area, ProjectTaskType Type)? Assignment => Role switch
    {
        SystemRole.CommercialAdvisor => (OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient),
        SystemRole.Budgets => (OperationalArea.Budgets, ProjectTaskType.PrepareQuotation),
        SystemRole.CommercialHead => (OperationalArea.Commercial, ProjectTaskType.ReviewQuotation),
        SystemRole.Engineering => (OperationalArea.Engineering, ProjectTaskType.PrepareOpeningRecord),
        SystemRole.Installation => (OperationalArea.Installation, ProjectTaskType.TakeMeasurements),
        SystemRole.Accounting => (OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit),
        _ => null
    };

    public bool Allows(OperationalArea area) => !string.IsNullOrWhiteSpace(Subject) && Assignment?.Area == area;

    public Expression<Func<ProjectTask, bool>> Filter()
    {
        if (Assignment is not { } assignment || string.IsNullOrWhiteSpace(Subject)) return task => false;
        return task => task.Area == assignment.Area && (task.Type == assignment.Type || (Role == SystemRole.CommercialAdvisor && (task.Type == ProjectTaskType.CorrectDepositReceipts || task.Type == ProjectTaskType.PresentOpeningRecord)) || (Role == SystemRole.Engineering && (task.Type == ProjectTaskType.ReviewOpeningRecord || task.Type == ProjectTaskType.FileOpeningRecord || task.Type == ProjectTaskType.EngineeringPackage))) && task.AssignedToGoogleSubject == Subject;
    }
}
