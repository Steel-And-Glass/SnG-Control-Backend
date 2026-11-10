// Persistence model. Application/API expose only the explicitly enabled incremental operations.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SnG.Control.Domain.Entities.Incremental;

public static class IncrementalEvidence
{
    public const string Complete = "COMPLETE";
    public const string LegacyIncomplete = "LEGACY_INCOMPLETE_EVIDENCE";
    public static string Hash(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    public static void RequireComplete(string status)
    {
        if (status != Complete) throw new InvalidOperationException("Complete frozen evidence is required.");
    }
    internal static void Json(string value) { using var document = JsonDocument.Parse(value); }
    internal static void HashValue(string value)
    {
        if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("SHA256 required.");
    }
}

public sealed class MeasurementSubmission
{
    private MeasurementSubmission() { }
    public MeasurementSubmission(Guid id, Guid projectId, Guid checklistId, int number, DateTime submittedAt, string submittedBySubject, string evidenceStatus, string? scopeIssue, string reportAvailability, string snapshotCanonical, string snapshotSha256, bool isLegacy, Guid? requestKey, string? fingerprint)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (checklistId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(checklistId));
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        if (submittedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(submittedAt));
        ArgumentException.ThrowIfNullOrWhiteSpace(submittedBySubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceStatus);
        if (evidenceStatus is not (IncrementalEvidence.Complete or IncrementalEvidence.LegacyIncomplete)) throw new ArgumentException("Unknown evidence status.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reportAvailability);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotCanonical);
        IncrementalEvidence.Json(snapshotCanonical);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        IncrementalEvidence.HashValue(snapshotSha256);
        if (requestKey == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(requestKey));
        if ((!isLegacy && (evidenceStatus != IncrementalEvidence.Complete || requestKey is null || fingerprint is null)) || reportAvailability is not ("PRESENT" or "ABSENT")) throw new ArgumentException("Invalid submission provenance.");
        if (!string.Equals(IncrementalEvidence.Hash(snapshotCanonical), snapshotSha256, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Snapshot hash mismatch.");
        Id = id;
        ProjectId = projectId;
        ChecklistId = checklistId;
        Number = number;
        SubmittedAt = submittedAt;
        SubmittedBySubject = submittedBySubject;
        EvidenceStatus = evidenceStatus;
        ScopeIssue = scopeIssue;
        ReportAvailability = reportAvailability;
        SnapshotCanonical = snapshotCanonical;
        SnapshotSha256 = snapshotSha256;
        IsLegacy = isLegacy;
        RequestKey = requestKey;
        Fingerprint = fingerprint;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ChecklistId { get; private set; }
    public int Number { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public string SubmittedBySubject { get; private set; } = "";
    public string EvidenceStatus { get; private set; } = "";
    public string? ScopeIssue { get; private set; }
    public string ReportAvailability { get; private set; } = "";
    public string SnapshotCanonical { get; private set; } = "";
    public string SnapshotSha256 { get; private set; } = "";
    public bool IsLegacy { get; private set; }
    public Guid? RequestKey { get; private set; }
    public string? Fingerprint { get; private set; }
}

public sealed class MeasurementAmendment
{
    private MeasurementAmendment() { }
    public MeasurementAmendment(Guid submissionId, Guid projectId, int version, string reason, DateTime at, string actorSubject, Guid requestKey, string fingerprint)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (at.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(at));
        ArgumentException.ThrowIfNullOrWhiteSpace(actorSubject);
        if (requestKey == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(requestKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (version < 2) throw new ArgumentException("An amendment starts at version 2.");
        SubmissionId = submissionId;
        ProjectId = projectId;
        Version = version;
        Reason = reason;
        At = at;
        ActorSubject = actorSubject;
        RequestKey = requestKey;
        Fingerprint = fingerprint;
    }
    public Guid SubmissionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Version { get; private set; }
    public string Reason { get; private set; } = "";
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
}

public sealed class MeasurementSubmissionMember
{
    private MeasurementSubmissionMember() { }
    public MeasurementSubmissionMember(Guid submissionId, Guid projectId, Guid structureId, int evidenceVersion, int? amendmentVersion, int position, string snapshotCanonical, string snapshotSha256)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        if (evidenceVersion < 1) throw new ArgumentOutOfRangeException(nameof(evidenceVersion));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotCanonical);
        IncrementalEvidence.Json(snapshotCanonical);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        IncrementalEvidence.HashValue(snapshotSha256);
        if (evidenceVersion == 1 ? amendmentVersion is not null : amendmentVersion != evidenceVersion) throw new ArgumentException("Amendment version mismatch.");
        if (!string.Equals(IncrementalEvidence.Hash(snapshotCanonical), snapshotSha256, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Snapshot hash mismatch.");
        using var evidence = JsonDocument.Parse(snapshotCanonical);
        if (!evidence.RootElement.TryGetProperty("Id", out var identity) || !identity.TryGetGuid(out var evidenceId) || evidenceId != structureId)
            throw new ArgumentException("Evidence must identify the original physical structure.");
        SubmissionId = submissionId;
        ProjectId = projectId;
        StructureId = structureId;
        EvidenceVersion = evidenceVersion;
        AmendmentVersion = amendmentVersion;
        Position = position;
        SnapshotCanonical = snapshotCanonical;
        SnapshotSha256 = snapshotSha256;
    }
    public Guid SubmissionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
    public int EvidenceVersion { get; private set; }
    public int? AmendmentVersion { get; private set; }
    public int Position { get; private set; }
    public string SnapshotCanonical { get; private set; } = "";
    public string SnapshotSha256 { get; private set; } = "";
}

public sealed class SubmissionReport
{
    private SubmissionReport() { }
    public SubmissionReport(Guid submissionId, Guid projectId, int version, Guid? legacyChecklistId, byte[]? pdf, string pdfSha256, string snapshotSha256, string? snapshotCanonical = null)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (legacyChecklistId == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(legacyChecklistId));
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfSha256);
        IncrementalEvidence.HashValue(pdfSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        IncrementalEvidence.HashValue(snapshotSha256);
        if ((legacyChecklistId is null) == (pdf is null) || pdf is { Length: 0 }) throw new ArgumentException("Exactly one report source required.");
        if (legacyChecklistId is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotCanonical);
            IncrementalEvidence.Json(snapshotCanonical);
            if (IncrementalEvidence.Hash(snapshotCanonical) != snapshotSha256.ToUpperInvariant()
                || Convert.ToHexString(SHA256.HashData(pdf!)) != pdfSha256.ToUpperInvariant())
                throw new ArgumentException("Report integrity mismatch.");
        }
        SnapshotCanonical = snapshotCanonical;
        SubmissionId = submissionId;
        ProjectId = projectId;
        Version = version;
        LegacyChecklistId = legacyChecklistId;
        Pdf = pdf;
        PdfSha256 = pdfSha256;
        SnapshotSha256 = snapshotSha256;
    }
    public string? SnapshotCanonical { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Version { get; private set; }
    public Guid? LegacyChecklistId { get; private set; }
    public byte[]? Pdf { get; private set; }
    public string PdfSha256 { get; private set; } = "";
    public string SnapshotSha256 { get; private set; } = "";
}

public sealed class SubmissionDocumentScope
{
    private SubmissionDocumentScope() { }
    public SubmissionDocumentScope(Guid submissionId, Guid projectId, Guid documentId, string declaration)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (documentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration);
        SubmissionId = submissionId;
        ProjectId = projectId;
        DocumentId = documentId;
        Declaration = declaration;
    }
    public Guid SubmissionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Declaration { get; private set; } = "";
}

public sealed class SubmissionDocumentMember
{
    private SubmissionDocumentMember() { }
    public SubmissionDocumentMember(Guid submissionId, Guid projectId, Guid documentId, Guid structureId, int evidenceVersion)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (documentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(documentId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        if (evidenceVersion < 1) throw new ArgumentOutOfRangeException(nameof(evidenceVersion));
        SubmissionId = submissionId;
        ProjectId = projectId;
        DocumentId = documentId;
        StructureId = structureId;
        EvidenceVersion = evidenceVersion;
    }
    public Guid SubmissionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid StructureId { get; private set; }
    public int EvidenceVersion { get; private set; }
}

public sealed class EngineeringWorkspace
{
    private EngineeringWorkspace() { }
    public EngineeringWorkspace(Guid projectId, DateTime createdAt)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (createdAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(createdAt));
        ProjectId = projectId;
        CreatedAt = createdAt;
    }
    public Guid ProjectId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}

public sealed class EngineeringReceipt
{
    private EngineeringReceipt() { }
    public EngineeringReceipt(Guid projectId, Guid submissionId, DateTime receivedAt)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (receivedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(receivedAt));
        ProjectId = projectId;
        SubmissionId = submissionId;
        ReceivedAt = receivedAt;
    }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public DateTime ReceivedAt { get; private set; }
}

public sealed class EngineeringEvidenceCheck
{
    private EngineeringEvidenceCheck() { }
    public EngineeringEvidenceCheck(Guid projectId, Guid submissionId, Guid structureId, int evidenceVersion, bool completed, long revision, DateTime? changedAt, string? changedBySubject, Guid? legacyChecklistId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        if (evidenceVersion < 1) throw new ArgumentOutOfRangeException(nameof(evidenceVersion));
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (changedAt is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("UTC required.", nameof(changedAt));
        if (legacyChecklistId == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(legacyChecklistId));
        if (completed && (changedAt is null || string.IsNullOrWhiteSpace(changedBySubject))) throw new ArgumentException("Completed check requires actor and time.");
        ProjectId = projectId;
        SubmissionId = submissionId;
        StructureId = structureId;
        EvidenceVersion = evidenceVersion;
        Completed = completed;
        Revision = revision;
        ChangedAt = changedAt;
        ChangedBySubject = changedBySubject;
        LegacyChecklistId = legacyChecklistId;
    }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid StructureId { get; private set; }
    public int EvidenceVersion { get; private set; }
    public bool Completed { get; private set; }
    public long Revision { get; private set; }
    public DateTime? ChangedAt { get; private set; }
    public string? ChangedBySubject { get; private set; }
    public Guid? LegacyChecklistId { get; private set; }
    public void SetCompleted(bool completed, DateTime at, string subject)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        if (LegacyChecklistId != null) throw new InvalidOperationException("Legacy checks use their original workflow.");
        Completed = completed; ChangedAt = at; ChangedBySubject = subject; Revision = checked(Revision + 1);
    }
}

public sealed class WorkPackageAssignment
{
    private WorkPackageAssignment() { }
    public WorkPackageAssignment(Guid id, Guid projectId, Guid packageId, int area, string subject, DateTime assignedAt, DateTime? endedAt, Guid? legacyTaskId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (packageId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(packageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (assignedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(assignedAt));
        if (endedAt is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("UTC required.", nameof(endedAt));
        if (legacyTaskId == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(legacyTaskId));
        if (area is not (1 or 4 or 5) || endedAt < assignedAt) throw new ArgumentException("Invalid package assignment.");
        Id = id;
        ProjectId = projectId;
        PackageId = packageId;
        Area = area;
        Subject = subject;
        AssignedAt = assignedAt;
        EndedAt = endedAt;
        LegacyTaskId = legacyTaskId;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid PackageId { get; private set; }
    public int Area { get; private set; }
    public string Subject { get; private set; } = "";
    public DateTime AssignedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public Guid? LegacyTaskId { get; private set; }
}

public sealed class WorkPackageMember
{
    private WorkPackageMember() { }
    public WorkPackageMember(Guid assignmentId, Guid projectId, Guid submissionId, Guid structureId, int evidenceVersion)
    {
        if (assignmentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(assignmentId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        if (evidenceVersion < 1) throw new ArgumentOutOfRangeException(nameof(evidenceVersion));
        AssignmentId = assignmentId;
        ProjectId = projectId;
        SubmissionId = submissionId;
        StructureId = structureId;
        EvidenceVersion = evidenceVersion;
    }
    public Guid AssignmentId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid StructureId { get; private set; }
    public int EvidenceVersion { get; private set; }
}

public sealed class OpeningRecord
{
    private OpeningRecord() { }
    public OpeningRecord(Guid id, Guid projectId, int number, DateTime createdAt, Guid? legacyChecklistId, string evidenceStatus)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        if (createdAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(createdAt));
        if (legacyChecklistId == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(legacyChecklistId));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceStatus);
        if (evidenceStatus is not (IncrementalEvidence.Complete or IncrementalEvidence.LegacyIncomplete)) throw new ArgumentException("Unknown evidence status.");
        Id = id;
        ProjectId = projectId;
        Number = number;
        CreatedAt = createdAt;
        LegacyChecklistId = legacyChecklistId;
        EvidenceStatus = evidenceStatus;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Number { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? LegacyChecklistId { get; private set; }
    public string EvidenceStatus { get; private set; } = "";
}

public sealed class OpeningRecordMember
{
    private OpeningRecordMember() { }
    public OpeningRecordMember(Guid openingRecordId, Guid projectId, Guid structureId, Guid submissionId, int evidenceVersion, int position)
    {
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        if (submissionId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(submissionId));
        if (evidenceVersion < 1) throw new ArgumentOutOfRangeException(nameof(evidenceVersion));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        OpeningRecordId = openingRecordId;
        ProjectId = projectId;
        StructureId = structureId;
        SubmissionId = submissionId;
        EvidenceVersion = evidenceVersion;
        Position = position;
    }
    public Guid OpeningRecordId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int EvidenceVersion { get; private set; }
    public int Position { get; private set; }
}

public sealed class ActiveOpeningRecordMember
{
    private ActiveOpeningRecordMember() { }
    public ActiveOpeningRecordMember(Guid openingRecordId, Guid projectId, Guid structureId)
    {
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        OpeningRecordId = openingRecordId;
        ProjectId = projectId;
        StructureId = structureId;
    }
    public Guid OpeningRecordId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
}

public sealed class OpeningRoundLink
{
    private OpeningRoundLink() { }
    public OpeningRoundLink(Guid roundId, Guid projectId, Guid openingRecordId, int number, Guid? legacyRoundId = null)
    {
        if (roundId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(roundId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        RoundId = roundId;
        ProjectId = projectId;
        OpeningRecordId = openingRecordId;
        Number = number; LegacyRoundId = legacyRoundId;
    }
    public Guid RoundId { get; private set; }
    public Guid? LegacyRoundId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid OpeningRecordId { get; private set; }
    public int Number { get; private set; }
}

public sealed class OpeningDocumentScope
{
    private OpeningDocumentScope() { }
    public OpeningDocumentScope(Guid openingRecordId, Guid projectId, Guid documentId, string kind, int version, string sha256, string declaration)
    {
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (documentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        IncrementalEvidence.HashValue(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration);
        OpeningRecordId = openingRecordId;
        ProjectId = projectId;
        DocumentId = documentId;
        Kind = kind;
        Version = version;
        Sha256 = sha256;
        Declaration = declaration;
    }
    public Guid OpeningRecordId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Kind { get; private set; } = "";
    public int Version { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string Declaration { get; private set; } = "";
}

public sealed class OpeningDocumentMember
{
    private OpeningDocumentMember() { }
    public OpeningDocumentMember(Guid openingRecordId, Guid projectId, Guid documentId, Guid structureId)
    {
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (documentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(documentId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        OpeningRecordId = openingRecordId;
        ProjectId = projectId;
        DocumentId = documentId;
        StructureId = structureId;
    }
    public Guid OpeningRecordId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid StructureId { get; private set; }
}

public sealed class OpeningAcceptance
{
    private OpeningAcceptance() { }
    public OpeningAcceptance(Guid id, Guid projectId, Guid openingRecordId, Guid roundId, Guid presentedDocumentId, Guid signedDocumentId, DateTime at, string actorSubject, bool isLegacy, Guid? requestKey, string? fingerprint)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (roundId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(roundId));
        if (presentedDocumentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(presentedDocumentId));
        if (signedDocumentId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(signedDocumentId));
        if (at.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(at));
        ArgumentException.ThrowIfNullOrWhiteSpace(actorSubject);
        if (requestKey == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(requestKey));
        Id = id;
        ProjectId = projectId;
        OpeningRecordId = openingRecordId;
        RoundId = roundId;
        PresentedDocumentId = presentedDocumentId;
        SignedDocumentId = signedDocumentId;
        At = at;
        ActorSubject = actorSubject;
        IsLegacy = isLegacy;
        RequestKey = requestKey;
        Fingerprint = fingerprint;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid OpeningRecordId { get; private set; }
    public Guid RoundId { get; private set; }
    public Guid PresentedDocumentId { get; private set; }
    public Guid SignedDocumentId { get; private set; }
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public bool IsLegacy { get; private set; }
    public Guid? RequestKey { get; private set; }
    public string? Fingerprint { get; private set; }
}

public sealed class OpeningAcceptanceMember
{
    private OpeningAcceptanceMember() { }
    public OpeningAcceptanceMember(Guid acceptanceId, Guid projectId, Guid openingRecordId, Guid structureId)
    {
        if (acceptanceId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(acceptanceId));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (openingRecordId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(openingRecordId));
        if (structureId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(structureId));
        AcceptanceId = acceptanceId;
        ProjectId = projectId;
        OpeningRecordId = openingRecordId;
        StructureId = structureId;
    }
    public Guid AcceptanceId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid OpeningRecordId { get; private set; }
    public Guid StructureId { get; private set; }
}

public sealed class OpeningFilingDelivery
{
    private OpeningFilingDelivery() { }
    public OpeningFilingDelivery(Guid id, Guid projectId, Guid acceptanceId, Guid? taskId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(projectId));
        if (acceptanceId == Guid.Empty) throw new ArgumentException("Identifier required.", nameof(acceptanceId));
        if (taskId == Guid.Empty) throw new ArgumentException("Empty identifier.", nameof(taskId));
        Id = id;
        ProjectId = projectId;
        AcceptanceId = acceptanceId;
        TaskId = taskId;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AcceptanceId { get; private set; }
    public Guid? TaskId { get; private set; }
}
