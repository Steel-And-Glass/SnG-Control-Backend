using System.Security.Cryptography;
using System.Text;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Domain.Entities;

public sealed class ProjectNotification
{
    private ProjectNotification() { }
    public ProjectNotification(ProjectTask task, NotificationKind kind, string title, string message, DateTime scheduledAt, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task.AssignedToGoogleSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(title); ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(kind) || kind >= NotificationKind.ProjectDocumentsUpdated) throw new ArgumentOutOfRangeException(nameof(kind));
        if (scheduledAt.Kind != DateTimeKind.Utc || createdAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamps required.");
        Id = Guid.NewGuid(); ProjectId = task.ProjectId; ProjectTaskId = task.Id;
        RecipientGoogleSubject = task.AssignedToGoogleSubject; Kind = kind; Title = title; Message = message;
        ScheduledAt = scheduledAt; CreatedAt = createdAt;
        State = kind == NotificationKind.Overdue ? NotificationState.Active : NotificationState.Informational;
        EventKey = Key(task, kind);
    }
    public static string Key(ProjectTask task, NotificationKind kind)
    {
        if (kind >= NotificationKind.ProjectDocumentsUpdated) throw new ArgumentException("Document and review notifications require an event id.");
        ArgumentException.ThrowIfNullOrWhiteSpace(task.AssignedToGoogleSubject);
        var recipient = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.AssignedToGoogleSubject)));
        var cycle = task.AssignedAt.Ticks - task.AssignedAt.Ticks % 10;
        return FormattableString.Invariant($"{task.Id:D}:{(int)kind}:{recipient}:{cycle}");
    }
    public static ProjectNotification DocumentsUpdated(ProjectTask task, string recipient, Guid batchId, int additions, int replacements, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        if (batchId == Guid.Empty || additions < 0 || replacements < 0 || additions + replacements == 0 || at.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid document event.");
        static string Count(int n) => $"{n} " + (n == 1 ? "documento" : "documentos");
        var changes = additions == 0 ? $"reemplazó {Count(replacements)}" : replacements == 0
            ? $"agregó {Count(additions)}" : $"agregó {Count(additions)} y reemplazó {Count(replacements)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient)));
        return new ProjectNotification
        {
            Id = Guid.NewGuid(),
            ProjectId = task.ProjectId,
            ProjectTaskId = task.Id,
            RecipientGoogleSubject = recipient,
            Kind = NotificationKind.ProjectDocumentsUpdated,
            State = NotificationState.Informational,
            Title = "Documentación del proyecto actualizada",
            Message = $"Comercial {changes}. Revisa la documentación actualizada.",
            ScheduledAt = at,
            CreatedAt = at,
            EventKey = $"{task.Id:D}:5:{hash}:{batchId:D}"
        };
    }
    public static ProjectNotification DepositReview(ProjectTask task, string recipient, Guid reviewId, NotificationKind kind, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        if (reviewId == Guid.Empty || at.Kind != DateTimeKind.Utc || kind is not (NotificationKind.DepositVerificationRequested or NotificationKind.DepositVerificationApproved or NotificationKind.DepositVerificationRejected))
            throw new ArgumentException("Invalid review event.");
        var title = kind switch { NotificationKind.DepositVerificationRequested => "Anticipo pendiente de verificación", NotificationKind.DepositVerificationApproved => "Anticipo aprobado", _ => "Anticipo rechazado" };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient)));
        return new ProjectNotification { Id = Guid.NewGuid(), ProjectId = task.ProjectId, ProjectTaskId = task.Id,
            RecipientGoogleSubject = recipient, Kind = kind, State = NotificationState.Informational, Title = title,
            Message = "Consulta los comprobantes y las observaciones en el proyecto.", ScheduledAt = at, CreatedAt = at,
            EventKey = $"{task.Id:D}:{(int)kind}:{hash}:{reviewId:D}" };
    }
    public void Resolve(DateTime completedAt)
    {
        if (Kind != NotificationKind.Overdue || completedAt.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only overdue notifications can be resolved at a valid UTC completion instant.");
        if (State == NotificationState.Resolved) return;
        State = NotificationState.Resolved; ResolvedAt = completedAt;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ProjectTaskId { get; private set; }
    public string RecipientGoogleSubject { get; private set; } = "";
    public NotificationKind Kind { get; private set; }
    public NotificationState State { get; private set; }
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public DateTime ScheduledAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public string EventKey { get; private set; } = "";
}
