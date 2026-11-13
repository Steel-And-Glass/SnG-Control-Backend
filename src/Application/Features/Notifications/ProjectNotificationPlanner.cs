using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Notifications;

public sealed class ProjectNotificationPlanner(TaskTiming timing)
{
    public IReadOnlyList<ProjectNotification> Reconcile(ProjectTask task, IReadOnlyList<ProjectNotification> history, DateTime now)
    {
        task.DetectOverdue(now);
        if (string.IsNullOrWhiteSpace(task.AssignedToGoogleSubject)) return [];
        var added = new List<ProjectNotification>();
        ProjectNotification Ensure(NotificationKind kind, DateTime scheduled)
        {
            var key = ProjectNotification.Key(task, kind);
            var existing = history.Concat(added).SingleOrDefault(n => n.EventKey == key);
            if (existing is not null) return existing;
            var title = Title(task.Type, kind);
            var notification = new ProjectNotification(task, kind, title, title + ". Consulte el proyecto para ver el detalle.", scheduled, now);
            added.Add(notification); return notification;
        }
        if (task.Status is ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress) Ensure(NotificationKind.TaskAssigned, task.AssignedAt);
        if (task.Status == ProjectTaskStatus.Completed)
        {
            if (task.DeadlineAt is DateTime deadline && task.CompletedAt is DateTime completed)
            {
                if (completed > deadline || task.WasOverdue == true) Ensure(NotificationKind.Overdue, deadline).Resolve(completed);
                else foreach (var alert in history.Where(n => n.ProjectTaskId == task.Id && n.Kind == NotificationKind.Overdue && n.State == NotificationState.Active)) alert.Resolve(completed);
            }
        }
        else if (task.Status == ProjectTaskStatus.InProgress && task.DeadlineAt is DateTime deadline)
        {
            if (now > deadline) Ensure(NotificationKind.Overdue, deadline);
            else if (now >= timing.DueToday(deadline)) Ensure(NotificationKind.DueToday, timing.DueToday(deadline));
            else if (now >= timing.DueSoon(deadline)) Ensure(NotificationKind.DueInTwoBusinessDays, timing.DueSoon(deadline));
        }
        return added;
    }

    public static string Title(ProjectTaskType type, NotificationKind kind) => (type, kind) switch
    {
        (ProjectTaskType.PrepareQuotation, NotificationKind.TaskAssigned) => "Nueva cotización asignada",
        (ProjectTaskType.PrepareQuotation, NotificationKind.DueInTwoBusinessDays) => "Cotización vence en 2 días laborales",
        (ProjectTaskType.PrepareQuotation, NotificationKind.DueToday) => "Cotización vence hoy",
        (ProjectTaskType.PrepareQuotation, NotificationKind.Overdue) => "Cotización atrasada",
        (ProjectTaskType.ReviewQuotation, NotificationKind.TaskAssigned) => "Nueva revisión de cotización asignada",
        (ProjectTaskType.ReviewQuotation, NotificationKind.DueInTwoBusinessDays) => "Revisión de cotización vence en 2 días laborales",
        (ProjectTaskType.ReviewQuotation, NotificationKind.DueToday) => "Revisión de cotización vence hoy",
        (ProjectTaskType.ReviewQuotation, NotificationKind.Overdue) => "Revisión de cotización atrasada",
        (ProjectTaskType.PresentQuotationToClient, NotificationKind.TaskAssigned) => "Nueva presentación al cliente asignada",
        (ProjectTaskType.PresentQuotationToClient, NotificationKind.DueInTwoBusinessDays) => "Presentación al cliente vence en 2 días laborales",
        (ProjectTaskType.PresentQuotationToClient, NotificationKind.DueToday) => "Presentación al cliente vence hoy",
        (ProjectTaskType.PresentQuotationToClient, NotificationKind.Overdue) => "Presentación al cliente atrasada",
        (ProjectTaskType.CorrectDepositReceipts, NotificationKind.TaskAssigned) => "Corregir comprobantes del anticipo",
        (ProjectTaskType.PresentOpeningRecord, NotificationKind.TaskAssigned) => "Presentar Acta de Vanos al cliente",
        (ProjectTaskType.ReviewOpeningRecord, NotificationKind.TaskAssigned) => "Revisar cambios del Acta de Vanos",
        (ProjectTaskType.EngineeringPackage, NotificationKind.TaskAssigned) => "Paquete de Ingenieria asignado",
        (ProjectTaskType.FileOpeningRecord, NotificationKind.TaskAssigned) => "Radicación de Vanos",
        (ProjectTaskType.PrepareOpeningRecord, NotificationKind.TaskAssigned) => "Elaborar Acta de Vanos",
        (ProjectTaskType.TakeMeasurements, NotificationKind.TaskAssigned) => "Toma de medidas asignada",
        (ProjectTaskType.RegisterInitialDeposit, NotificationKind.TaskAssigned) => "Registro de anticipo inicial asignado",
        _ => throw new ArgumentOutOfRangeException(nameof(type), "No operational notification text is defined.")
    };
}
