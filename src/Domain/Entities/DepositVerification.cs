using SnG.Control.Domain.Enums;

namespace SnG.Control.Domain.Entities;

public enum DepositVerificationStatus { Pending = 1, Approved = 2, Rejected = 3 }

public sealed class DepositVerification
{
    private DepositVerification() { }
    public DepositVerification(AccountingHandoff handoff, Guid requestKey, DateTime at, string subject)
    {
        if (requestKey == Guid.Empty || at.Kind != DateTimeKind.Utc) throw new ArgumentException("Valid request and UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = Guid.NewGuid(); ProjectId = handoff.ProjectId; AccountingHandoffId = handoff.Id;
        RequestKey = requestKey; SubmittedAt = at; SubmittedByGoogleSubject = subject;
        Status = DepositVerificationStatus.Pending;
    }
    public void Decide(bool approved, string? note, DateTime at, string subject, string? email, string? name)
    {
        if (Status != DepositVerificationStatus.Pending) throw new InvalidOperationException("Review already decided.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if ((!approved && note is null) || note?.Length > 2000) throw new ArgumentException("Valid observation required.");
        if (at.Kind != DateTimeKind.Utc || at < SubmittedAt) throw new ArgumentException("Valid UTC decision required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Status = approved ? DepositVerificationStatus.Approved : DepositVerificationStatus.Rejected;
        Note = note; DecidedAt = at; DecidedByGoogleSubject = subject; DecidedByEmail = email; DecidedByName = name;
    }
    public void AssignCorrectionTask(ProjectTask task)
    {
        if (Status != DepositVerificationStatus.Rejected || CorrectionTaskId is not null
            || task.ProjectId != ProjectId || task.Type != ProjectTaskType.CorrectDepositReceipts
            || task.Status != ProjectTaskStatus.InProgress || task.AssignedToGoogleSubject != SubmittedByGoogleSubject)
            throw new InvalidOperationException("A rejected review requires its advisor correction task.");
        CorrectionTaskId = task.Id;
    }
    public Guid? CorrectionTaskId { get; private set; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AccountingHandoffId { get; private set; }
    public Guid RequestKey { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public string SubmittedByGoogleSubject { get; private set; } = "";
    public DepositVerificationStatus Status { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecidedByGoogleSubject { get; private set; }
    public string? DecidedByEmail { get; private set; }
    public string? DecidedByName { get; private set; }
    public string? Note { get; private set; }
}

public sealed class DepositVerificationDocument
{
    private DepositVerificationDocument() { }
    public DepositVerificationDocument(DepositVerification review, ProjectDocument document)
    {
        if (review.ProjectId != document.ProjectId || !document.IsPaymentReceipt || !document.IsCurrent)
            throw new ArgumentException("Current receipt in the same project required.");
        DepositVerificationId = review.Id; ProjectDocumentId = document.Id; ProjectId = review.ProjectId;
    }
    public Guid DepositVerificationId { get; private set; }
    public Guid ProjectDocumentId { get; private set; }
    public Guid ProjectId { get; private set; }
}
