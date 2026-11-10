namespace SnG.Control.Application.Features.IncrementalFlow;

// Internal handoff contracts only. No controllers, commands or authorization flows are registered.
public sealed record FrozenStructureReference(Guid SubmissionId, Guid StructureId, int EvidenceVersion, int Position);
public sealed record SubmissionEvidenceReference(Guid SubmissionId, Guid ProjectId, int Number, string EvidenceStatus,
    string? ScopeIssue, string ReportAvailability, string SnapshotSha256, IReadOnlyList<FrozenStructureReference> Members);
public sealed record OpeningRecordReference(Guid OpeningRecordId, Guid ProjectId, int Number,
    IReadOnlyList<FrozenStructureReference> Members);
public sealed record AcceptanceScopeReference(Guid AcceptanceId, Guid OpeningRecordId, Guid RoundId,
    Guid PresentedDocumentId, Guid SignedDocumentId, IReadOnlyList<Guid> StructureIds);
public sealed record PackageResponsibilityReference(Guid PackageId, Guid AssignmentId, Guid ProjectId,
    int Area, string GoogleSubject, DateTime AssignedAt, DateTime? EndedAt);