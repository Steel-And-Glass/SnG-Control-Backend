using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Domain.Entities;

public sealed class InitialDeposit
{
    private InitialDeposit() { }
    public InitialDeposit(AccountingHandoff handoff, decimal amount, DateTime receivedAt, DateTime recordedAt, string? reference, string? note, string subject, string? email, string? name)
    {
        if (amount <= 0 || amount > Money.Maximum || Money.Normalize(amount) <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (receivedAt.Kind != DateTimeKind.Utc || recordedAt.Kind != DateTimeKind.Utc || receivedAt > recordedAt) throw new ArgumentException("Valid UTC receipt time required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(); note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reference?.Length > 200 || note?.Length > 2000) throw new ArgumentException("Text exceeds limits.");
        Id = Guid.NewGuid(); ProjectId = handoff.ProjectId; AccountingHandoffId = handoff.Id; CommercialQuotationId = handoff.CommercialQuotationId;
        RegisterInitialDepositTaskId = handoff.RegisterInitialDepositTaskId; Amount = Money.Normalize(amount);
        ReceivedAt = receivedAt; RecordedAt = recordedAt; Reference = reference; Note = note;
        RecordedByGoogleSubject = subject; RecordedByEmail = email; RecordedByName = name;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AccountingHandoffId { get; private set; }
    public Guid CommercialQuotationId { get; private set; }
    public Guid RegisterInitialDepositTaskId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode => Money.CurrencyCode;
    public DateTime ReceivedAt { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public string RecordedByGoogleSubject { get; private set; } = null!;
    public string? RecordedByEmail { get; private set; }
    public string? RecordedByName { get; private set; }
}
