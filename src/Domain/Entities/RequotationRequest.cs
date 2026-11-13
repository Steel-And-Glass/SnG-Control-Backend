namespace SnG.Control.Domain.Entities;

public sealed class RequotationRequest
{
    private RequotationRequest() { }
    public RequotationRequest(Guid projectId, Guid sourceDecisionId, int targetQuotationRound, Guid prepareTaskId,
        string requestedChanges, DateTime requestedAt, string subject, string? email, string? name)
    {
        if (projectId == Guid.Empty || sourceDecisionId == Guid.Empty || prepareTaskId == Guid.Empty) throw new ArgumentException("Required relationships.");
        if (targetQuotationRound <= 1) throw new ArgumentOutOfRangeException(nameof(targetQuotationRound));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedChanges);
        requestedChanges = requestedChanges.Trim();
        if (requestedChanges.Length > 4000) throw new ArgumentException("Changes exceed 4000 characters.");
        if (requestedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = Guid.NewGuid(); ProjectId = projectId; SourceClientQuotationDecisionId = sourceDecisionId;
        TargetQuotationRound = targetQuotationRound; PrepareQuotationTaskId = prepareTaskId;
        RequestedChanges = requestedChanges; RequestedAt = requestedAt;
        RequestedByGoogleSubject = subject; RequestedByEmail = email; RequestedByName = name;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid SourceClientQuotationDecisionId { get; private set; }
    public int TargetQuotationRound { get; private set; }
    public Guid PrepareQuotationTaskId { get; private set; }
    public string RequestedChanges { get; private set; } = null!;
    public DateTime RequestedAt { get; private set; }
    public string RequestedByGoogleSubject { get; private set; } = null!;
    public string? RequestedByEmail { get; private set; }
    public string? RequestedByName { get; private set; }
}
