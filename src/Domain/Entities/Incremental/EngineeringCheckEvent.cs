namespace SnG.Control.Domain.Entities.Incremental;
public sealed class EngineeringCheckEvent
{
    private EngineeringCheckEvent() { }
    public EngineeringCheckEvent(EngineeringEvidenceCheck check, Guid requestKey, string fingerprint, string? email, string? name)
    {
        if (requestKey == Guid.Empty || check.ChangedAt == null || check.ChangedBySubject == null) throw new ArgumentException("Recorded check required.");
        IncrementalEvidence.HashValue(fingerprint);
        ProjectId = check.ProjectId; SubmissionId = check.SubmissionId; StructureId = check.StructureId; EvidenceVersion = check.EvidenceVersion;
        RequestKey = requestKey; Fingerprint = fingerprint; Completed = check.Completed; Revision = check.Revision;
        At = check.ChangedAt.Value; ActorSubject = check.ChangedBySubject; ActorEmail = email; ActorName = name;
    }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid StructureId { get; private set; }
    public int EvidenceVersion { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public bool Completed { get; private set; }
    public long Revision { get; private set; }
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string? ActorEmail { get; private set; }
    public string? ActorName { get; private set; }
}
