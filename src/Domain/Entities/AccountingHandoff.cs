namespace SnG.Control.Domain.Entities;

public sealed class AccountingHandoff
{
    private AccountingHandoff() { }
    public AccountingHandoff(Guid projectId, Guid decisionId, Guid quotationId, int round, Guid taskId, DateTime sentAt, string subject, string? email, string? name)
    {
        if (new[] { projectId, decisionId, quotationId, taskId }.Any(id => id == Guid.Empty)) throw new ArgumentException("Required relationships.");
        if (round <= 0) throw new ArgumentOutOfRangeException(nameof(round));
        if (sentAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = Guid.NewGuid(); ProjectId = projectId; SourceClientQuotationDecisionId = decisionId; CommercialQuotationId = quotationId;
        QuotationRound = round; RegisterInitialDepositTaskId = taskId; SentAt = sentAt;
        SentByGoogleSubject = subject; SentByEmail = email; SentByName = name;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid SourceClientQuotationDecisionId { get; private set; }
    public Guid CommercialQuotationId { get; private set; }
    public int QuotationRound { get; private set; }
    public Guid RegisterInitialDepositTaskId { get; private set; }
    public DateTime SentAt { get; private set; }
    public string SentByGoogleSubject { get; private set; } = null!;
    public string? SentByEmail { get; private set; }
    public string? SentByName { get; private set; }
}
