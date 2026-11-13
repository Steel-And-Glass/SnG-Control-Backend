namespace SnG.Control.Domain.Entities;

public enum OpeningRoundStatus { Ready = 1, Presented = 2, ChangesRequested = 3, Revised = 4, Accepted = 5 }
public enum OpeningApproval { AwaitingApproval = 1, Confirmed = 2, ChangeRequested = 3 }
public enum OpeningDocumentKind { Revised = 1, Signed = 2 }

public sealed class OpeningRecordRound
{
    private OpeningRecordRound() { }
    public OpeningRecordRound(EngineeringChecklist source, int number, Guid taskId, Guid documentId, DateTime at)
    {
        if (source.CompletedAt == null || number < 1 || taskId == Guid.Empty || documentId == Guid.Empty) throw new ArgumentException("Completed engineering source required.");
        Utc(at); Id = Guid.NewGuid(); ProjectId = source.ProjectId; ChecklistId = source.Id; SubmissionId = source.SubmissionId;
        Number = number; PresentationTaskId = taskId; PresentedDocumentId = documentId; ReceivedAt = at;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int Number { get; private set; }
    public Guid PresentationTaskId { get; private set; }
    public Guid PresentedDocumentId { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public OpeningRoundStatus Status { get; private set; } = OpeningRoundStatus.Ready;
    public long Revision { get; private set; }
    public DateTime? PresentedAt { get; private set; }
    public string? PresentedBySubject { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public string? RespondedBySubject { get; private set; }
    public string? GeneralObservations { get; private set; }
    public Guid? ReviewTaskId { get; private set; }
    public bool AffectedDeclared { get; private set; }
    public Guid? RevisedDocumentId { get; private set; }
    public Guid? SignedDocumentId { get; private set; }
    public Guid? FilingTaskId { get; private set; }
    public DateTime? ReviewCompletedAt { get; private set; }
    public string? ReviewCompletedBySubject { get; private set; }
    public string? FinalSnapshotJson { get; private set; }
    public void DeclareAffected() { Require(OpeningRoundStatus.ChangesRequested); AffectedDeclared = true; Advance(); }
    public void Advance() => Revision = checked(Revision + 1);
    public void Require(OpeningRoundStatus status) { if (Status != status) throw new InvalidOperationException("OpeningRoundStateConflict"); }
    public void Present(DateTime at, string subject)
    {
        Require(OpeningRoundStatus.Ready); Actor(at, subject); if (at < ReceivedAt) throw new InvalidOperationException("OpeningRoundTimeConflict");
        PresentedAt = at; PresentedBySubject = subject; Status = OpeningRoundStatus.Presented; Advance();
    }
    public void SetDocument(Guid document, OpeningDocumentKind kind)
    {
        if (document == Guid.Empty) throw new ArgumentException("Document required.");
        Require(kind == OpeningDocumentKind.Signed ? OpeningRoundStatus.Presented : OpeningRoundStatus.ChangesRequested);
        if (kind == OpeningDocumentKind.Signed) SignedDocumentId = document; else RevisedDocumentId = document;
        Advance();
    }
    public void RequestChanges(DateTime at, string subject, Guid reviewTask, string? observations)
    {
        Respond(at, subject); if (reviewTask == Guid.Empty) throw new ArgumentException("Review task required.");
        Status = OpeningRoundStatus.ChangesRequested; ReviewTaskId = reviewTask; GeneralObservations = observations; Advance();
    }
    public void Accept(DateTime at, string subject, Guid filingTask, string snapshot)
    {
        if (SignedDocumentId == null || filingTask == Guid.Empty) throw new InvalidOperationException("OpeningSignedActaRequired");
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot); Respond(at, subject);
        Status = OpeningRoundStatus.Accepted; FilingTaskId = filingTask; FinalSnapshotJson = snapshot; Advance();
    }
    private void Respond(DateTime at, string subject)
    {
        Require(OpeningRoundStatus.Presented); Actor(at, subject);
        if (PresentedAt == null || at < PresentedAt) throw new InvalidOperationException("OpeningRoundTimeConflict");
        RespondedAt = at; RespondedBySubject = subject;
    }
    public void CompleteReview(DateTime at, string subject, string snapshot)
    {
        Require(OpeningRoundStatus.ChangesRequested); Actor(at, subject);
        if (RevisedDocumentId == null || at < RespondedAt) throw new InvalidOperationException("OpeningRevisedActaRequired");
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot); Status = OpeningRoundStatus.Revised;
        ReviewCompletedAt = at; ReviewCompletedBySubject = subject; FinalSnapshotJson = snapshot; Advance();
    }
    private static void Actor(DateTime at, string subject) { Utc(at); ArgumentException.ThrowIfNullOrWhiteSpace(subject); }
    private static void Utc(DateTime at) { if (at.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required."); }
}

public sealed class OpeningRecordStructure
{
    private OpeningRecordStructure() { }
    public OpeningRecordStructure(Guid roundId, Guid structureId, OpeningRecordStructure? previous = null)
    {
        if (roundId == Guid.Empty || structureId == Guid.Empty) throw new ArgumentException("Structure required.");
        RoundId = roundId; StructureId = structureId;
        if (previous is { Approval: OpeningApproval.Confirmed, Affected: false })
        { Approval = OpeningApproval.Confirmed; ApprovalDocumentId = previous.ApprovalDocumentId; CarriedApproval = true; }
    }
    public Guid RoundId { get; private set; }
    public Guid StructureId { get; private set; }
    public OpeningApproval Approval { get; private set; } = OpeningApproval.AwaitingApproval;
    public Guid? ApprovalDocumentId { get; private set; }
    public bool CarriedApproval { get; private set; }
    public string? ChangeDescription { get; private set; }
    public bool Affected { get; private set; }
    public bool ReviewCompleted { get; private set; }
    public long Revision { get; private set; }
    public void Confirm(OpeningRecordRound round, bool confirmed)
    {
        round.Require(OpeningRoundStatus.Presented);
        if (CarriedApproval || Approval == OpeningApproval.ChangeRequested) throw new InvalidOperationException("OpeningApprovalLocked");
        Approval = confirmed ? OpeningApproval.Confirmed : OpeningApproval.AwaitingApproval;
        ApprovalDocumentId = confirmed ? round.PresentedDocumentId : null; Revision++;
    }
    public void RequestChange(OpeningRecordRound round, string description)
    {
        round.Require(OpeningRoundStatus.Presented); ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (Approval != OpeningApproval.AwaitingApproval) throw new InvalidOperationException("OpeningApprovalLocked");
        ChangeDescription = description; Approval = OpeningApproval.ChangeRequested; Affected = true; Revision++;
    }
    public void DeclareAffected(OpeningRecordRound round, bool affected)
    {
        round.Require(OpeningRoundStatus.ChangesRequested);
        if (ChangeDescription != null && !affected) throw new InvalidOperationException("OpeningAffectedStructuresRequired");
        if (Affected != affected) { Affected = affected; ReviewCompleted = false; Revision++; }
    }
    public void CheckReview(OpeningRecordRound round, bool completed)
    {
        round.Require(OpeningRoundStatus.ChangesRequested);
        if (!Affected) throw new InvalidOperationException("OpeningStructureNotInReview");
        ReviewCompleted = completed; Revision++;
    }
}

public sealed class OpeningRecordDocument
{
    private OpeningRecordDocument() { }
    public OpeningRecordDocument(OpeningRecordRound round, ProjectDocument document, OpeningDocumentKind kind, int version, string hash, string affectedIdsJson)
    {
        if (!document.IsEngineeringDocument || round.ProjectId != document.ProjectId || !Enum.IsDefined(kind) || version < 1 || hash.Length != 64) throw new ArgumentException("Invalid acta.");
        DocumentId = document.Id; RoundId = round.Id; ProjectId = round.ProjectId; Kind = kind; Version = version; Sha256 = hash; AffectedIdsJson = affectedIdsJson;
    }
    public Guid DocumentId { get; private set; }
    public Guid RoundId { get; private set; }
    public Guid ProjectId { get; private set; }
    public OpeningDocumentKind Kind { get; private set; }
    public int Version { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string AffectedIdsJson { get; private set; } = "[]";
}

public sealed class OpeningRecordEvent
{
    private OpeningRecordEvent() { }
    public OpeningRecordEvent(OpeningRecordRound round, Guid key, string fingerprint, string action, string details, DateTime at, string subject, string? name)
    { Id = Guid.NewGuid(); RoundId = round.Id; RequestKey = key; Fingerprint = fingerprint; Action = action; DetailsJson = details; At = at; ActorSubject = subject; ActorName = name; Revision = round.Revision; }
    public Guid Id { get; private set; }
    public Guid RoundId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string DetailsJson { get; private set; } = "{}";
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string? ActorName { get; private set; }
    public long Revision { get; private set; }
}
