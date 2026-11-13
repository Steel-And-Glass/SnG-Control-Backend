namespace SnG.Control.Domain.Entities;

public sealed class EngineeringChecklist
{
    private EngineeringChecklist() { }
    public EngineeringChecklist(Guid projectId, Guid measurementChecklistId, Guid submissionId, Guid taskId, DateTime at)
    {
        if (new[] { projectId, measurementChecklistId, submissionId, taskId }.Contains(Guid.Empty) || at.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid engineering source.");
        Id = Guid.NewGuid(); ProjectId = projectId; MeasurementChecklistId = measurementChecklistId; SubmissionId = submissionId; TaskId = taskId; CreatedAt = at;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid MeasurementChecklistId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid TaskId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public long Revision { get; private set; }
    public Guid? CurrentDocumentId { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? CompletedBySubject { get; private set; }
    public string? FinalSnapshotJson { get; private set; }
    public void Advance() { RequireOpen(); Revision = checked(Revision + 1); }
    public void SetDocument(Guid documentId) { if (documentId == Guid.Empty) throw new ArgumentException("Document required."); Advance(); CurrentDocumentId = documentId; }
    public void Complete(DateTime at, string subject, string snapshot)
    {
        RequireOpen(); if (CurrentDocumentId == null || at.Kind != DateTimeKind.Utc || at < CreatedAt) throw new InvalidOperationException("Engineering incomplete.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject); ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        Advance(); CompletedAt = at; CompletedBySubject = subject; FinalSnapshotJson = snapshot;
    }
    public void RequireOpen() { if (CompletedAt != null) throw new InvalidOperationException("Engineering closed."); }
}
public sealed class EngineeringStructure
{
    private EngineeringStructure() { }
    public EngineeringStructure(EngineeringChecklist checklist, Guid structureId, int position, string snapshot)
    {
        if (structureId == Guid.Empty || position < 0) throw new ArgumentException("Invalid structure.");
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; StructureId = structureId; Position = position; SnapshotJson = snapshot;
    }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
    public int Position { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    public long Revision { get; private set; }
    public bool Completed { get; private set; }
    public DateTime? ChangedAt { get; private set; }
    public string? ChangedBySubject { get; private set; }
    public void SetCompleted(bool completed, DateTime at, string subject)
    {
        if (at.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Completed = completed; ChangedAt = at; ChangedBySubject = subject; Revision = checked(Revision + 1);
    }
}
public sealed class EngineeringEvent
{
    private EngineeringEvent() { }
    public EngineeringEvent(EngineeringChecklist checklist, Guid key, string fingerprint, string action, Guid? structureId, DateTime at, string subject, string? email, string? name, string? reason = null)
    {
        Id = Guid.NewGuid(); ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; RequestKey = key; Fingerprint = fingerprint; Action = action;
        StructureId = structureId; At = at; ActorSubject = subject; ActorEmail = email; ActorName = name; Revision = checklist.Revision; Reason = reason;
    }
    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Action { get; private set; } = "";
    public Guid? StructureId { get; private set; }
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string? ActorEmail { get; private set; }
    public string? ActorName { get; private set; }
    public long Revision { get; private set; }
    public string? Reason { get; private set; }
}
public sealed class EngineeringActa
{
    private EngineeringActa() { }
    public EngineeringActa(EngineeringChecklist checklist, ProjectDocument document, int version, string hash)
    {
        if (!document.IsEngineeringDocument || document.ProjectId != checklist.ProjectId || version <= 0 || hash.Length != 64) throw new ArgumentException("Invalid opening record.");
        DocumentId = document.Id; ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; Version = version; Sha256 = hash;
    }
    public Guid DocumentId { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Version { get; private set; }
    public string Sha256 { get; private set; } = "";
}
