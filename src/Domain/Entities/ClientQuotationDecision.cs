using SnG.Control.Domain.Enums;

namespace SnG.Control.Domain.Entities;

public sealed class ClientQuotationDecision
{
    private ClientQuotationDecision() { }
    public ClientQuotationDecision(Guid projectId, Guid commercialQuotationId, Guid presentQuotationTaskId,
        ClientQuotationDecisionType decision, string? note, DateTime recordedAt, string subject, string? email, string? name)
    {
        if (projectId == Guid.Empty || commercialQuotationId == Guid.Empty || presentQuotationTaskId == Guid.Empty)
            throw new ArgumentException("Decision relationships are required.");
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        if (note?.Length > 2000) throw new ArgumentException("Note exceeds 2000 characters.", nameof(note));
        if (recordedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC instant is required.", nameof(recordedAt));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = Guid.NewGuid(); ProjectId = projectId; CommercialQuotationId = commercialQuotationId;
        PresentQuotationTaskId = presentQuotationTaskId; Decision = decision; Note = note;
        RecordedAt = recordedAt; RecordedByGoogleSubject = subject; RecordedByEmail = email; RecordedByName = name;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CommercialQuotationId { get; private set; }
    public Guid PresentQuotationTaskId { get; private set; }
    public ClientQuotationDecisionType Decision { get; private set; }
    public string? Note { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string RecordedByGoogleSubject { get; private set; } = null!;
    public string? RecordedByEmail { get; private set; }
    public string? RecordedByName { get; private set; }
}
