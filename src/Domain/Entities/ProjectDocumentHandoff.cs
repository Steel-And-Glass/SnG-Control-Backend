namespace SnG.Control.Domain.Entities;

public sealed class ProjectDocumentHandoff
{
    private ProjectDocumentHandoff() { }
    public ProjectDocumentHandoff(Guid projectId, DateTime sentAt, string subject, string? email, string? name)
    {
        if (projectId == Guid.Empty || sentAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Project and UTC timestamp required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = Guid.NewGuid(); ProjectId = projectId; SentAt = sentAt;
        SentByGoogleSubject = subject; SentByEmail = email; SentByName = name;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateTime SentAt { get; private set; }
    public string SentByGoogleSubject { get; private set; } = "";
    public string? SentByEmail { get; private set; }
    public string? SentByName { get; private set; }
}

public sealed class ProjectDocumentHandoffItem
{
    private ProjectDocumentHandoffItem() { }
    public ProjectDocumentHandoffItem(ProjectDocumentHandoff handoff, ProjectDocument document)
    {
        if (handoff.ProjectId != document.ProjectId || !document.IsCurrent) throw new ArgumentException("Current project document required.");
        HandoffId = handoff.Id; ProjectId = handoff.ProjectId; ProjectDocumentId = document.Id;
    }
    public Guid HandoffId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ProjectDocumentId { get; private set; }
}
