using SnG.Control.Application.Features.ProjectDocuments;
namespace SnG.Control.Application.Features.Measurements;

// Persisted on checklist creation only. Deployment must set a future UTC cutoff explicitly.
public sealed record IncrementalMeasurementActivation(DateTime? NewProjectsFromUtc = null)
{
    public bool Includes(DateTime projectCreatedAt) => NewProjectsFromUtc is { Kind: DateTimeKind.Utc } cutoff && projectCreatedAt >= cutoff;
}
public sealed record MeasurementDocumentSelection(Guid DocumentId, Guid[] StructureIds);
public sealed record MeasurementSubmissionCommand(Guid RequestKey, long ExpectedRevision, Guid[] StructureIds, MeasurementDocumentSelection[] Documents)
{
    public void Validate()
    {
        static bool Ids(Guid[]? ids) => ids is { Length: > 0 and <= 500 } && ids.All(i => i != Guid.Empty) && ids.Distinct().Count() == ids.Length;
        if (RequestKey == Guid.Empty || ExpectedRevision < 0 || !Ids(StructureIds) || Documents == null || Documents.Length > SnG.Control.Domain.Entities.ProjectDocument.MaxCurrentDocuments ||
            Documents.Any(d => d == null || d.DocumentId == Guid.Empty || !Ids(d.StructureIds) || d.StructureIds.Any(i => !StructureIds.Contains(i))) ||
            Documents.Select(d => d.DocumentId).Distinct().Count() != Documents.Length)
            throw new DocumentException(400, "InvalidMeasurementCommand");
    }
}
public sealed record MeasurementDeliveryDocument(Guid DocumentId, string? FileName, string? Sha256, Guid[] StructureIds, DateTime? AssociatedAt, string? AssociatedBySubject);
public sealed record MeasurementDelivery(Guid Id, int Number, DateTime SubmittedAt, string SubmittedBySubject, string EvidenceStatus,
    string? ScopeIssue, string ReportAvailability, IReadOnlyList<Guid> StructureIds, IReadOnlyList<MeasurementDeliveryDocument> Documents);
