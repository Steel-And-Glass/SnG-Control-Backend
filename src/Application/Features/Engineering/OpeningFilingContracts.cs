using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Engineering;

public sealed class OpeningFilingLimits
{
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public long MaxProjectStorageBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}

public sealed record OpeningFilingOp(Guid StructureId, string Code, bool Confirmed);
public sealed record OpeningFilingCommand(Guid RequestKey, long ExpectedRevision, string Action, string? Number, IReadOnlyList<OpeningFilingOp>? Members, Guid? DocumentId, string? Reason);
public sealed record OpeningFilingUpload(Guid RequestKey, long ExpectedRevision, string Category, Guid? ReplacesDocumentId);
public sealed record OpeningFilingDocumentView(Guid DocumentId, string Category, int Version, string State, string Name, long SizeBytes, string Sha256, DateTime UploadedAt, string UploadedBySubject, string? ContentType);
public sealed record OpeningFilingDownload(Stream Content, string FileName, string ContentType);
public sealed record OpeningFilingVersionView(Guid Id, int Number, string? Reason, DateTime CreatedAt, string CreatedBySubject, DateTime? ConfirmedAt, string? ConfirmedBySubject, string? ManifestSha256, IReadOnlyList<OpeningFilingOp> Members, IReadOnlyList<OpeningFilingDocumentView> Documents);
public sealed record OpeningFilingSource(Guid StructureId, Guid SubmissionId, int EvidenceVersion, SnG.Control.Application.Features.Measurements.MeasurementStructureView Source);
public sealed record OpeningFilingHistory(string Action, DateTime At, string ActorSubject);
public sealed record OpeningFilingView(Guid Id, Guid? TaskId, Guid AcceptanceId, Guid OpeningRecordId, int ActaNumber, Guid PresentedDocumentId, Guid SignedDocumentId, DateTime AcceptedAt, string ResponsibleSubject, string Status, string? Number, long Revision, bool CanModify, string? BlockedReason, IReadOnlyList<OpeningFilingSource> Structures, IReadOnlyList<OpeningFilingVersionView> Versions, IReadOnlyList<OpeningFilingHistory> History);
public sealed record OpeningFilingsView(string ProjectName, string? SgCode, IReadOnlyList<OpeningFilingView> Filings);
public interface IOpeningFilingStore
{
    Task<OpeningFilingsView?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token);
    Task CommandAsync(Guid id, Guid filingId, OpeningFilingCommand command, DecisionActor actor, CancellationToken token);
    Task UploadAsync(Guid id, Guid filingId, OpeningFilingUpload manifest, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token);
    Task<OpeningFilingDownload> DownloadAsync(Guid id, Guid filingId, Guid versionId, Guid documentId, ProjectReadScope scope, CancellationToken token);
}
public sealed class OpeningFilingService(IOpeningFilingStore store, ICurrentUser current, IActiveRoleContext active, IEngineeringAssigneeIdentity engineering)
{
    private DecisionActor Actor => new(current.Subject, current.Email, current.Name);
    public void RequireWrite()
    {
        if (active.Role != SystemRole.Engineering || !engineering.Subjects.Contains(current.Subject, StringComparer.Ordinal))
            throw new DocumentException(403, "EngineeringAccessDenied");
    }
    public Task<OpeningFilingsView?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, new(current.Subject, active.Role), token);
    public Task CommandAsync(Guid id, Guid filingId, OpeningFilingCommand c, CancellationToken token)
    {
        RequireWrite();
        var valid = c.Action switch {
            "Save" => c.Number is { Length: > 0 and <= 80 } && c.Members is { Count: <= 500 } && c.DocumentId == null && c.Reason == null,
            "Withdraw" => c.DocumentId is { } d && d != Guid.Empty && c.Number == null && c.Members == null && c.Reason == null,
            "Confirm" => c.DocumentId == null && c.Number == null && c.Members == null && c.Reason == null,
            "Amend" => c.DocumentId == null && c.Number == null && c.Members == null && !string.IsNullOrWhiteSpace(c.Reason) && c.Reason.Length <= 2000,
            _ => false };
        if (!valid || c.RequestKey == Guid.Empty || c.ExpectedRevision < 0) throw new DocumentException(400, "InvalidOpeningFilingCommand");
        return store.CommandAsync(id, filingId, c, Actor, token);
    }
    public Task UploadAsync(Guid id, Guid filingId, OpeningFilingUpload manifest, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token)
    {
        RequireWrite();
        if (manifest.RequestKey == Guid.Empty || manifest.ExpectedRevision < 0 || !SnG.Control.Domain.Entities.Incremental.OpeningFilingRules.Categories.Contains(manifest.Category))
            throw new DocumentException(400, "InvalidOpeningFilingCommand");
        return store.UploadAsync(id, filingId, manifest, uploads, Actor, token);
    }
    public Task<OpeningFilingDownload> DownloadAsync(Guid id, Guid filingId, Guid versionId, Guid documentId, CancellationToken token) => store.DownloadAsync(id, filingId, versionId, documentId, new(current.Subject, active.Role), token);
}
