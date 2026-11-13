using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Engineering;

public sealed record OpeningChange(Guid StructureId, string Description);
public sealed record OpeningCommand(Guid RequestKey, long ExpectedRevision, string Action, Guid? StructureId = null,
    bool? Value = null, IReadOnlyList<OpeningChange>? Changes = null, string? Observations = null,
    IReadOnlyList<Guid>? AffectedStructureIds = null, Guid? DocumentId = null, bool? AttestSignedForPresentedVersion = null);
public sealed record OpeningRow(Guid StructureId, string Approval, Guid? ApprovalDocumentId, bool CarriedApproval,
    string? ChangeDescription, bool Affected, bool ReviewCompleted, long Revision);
public sealed record OpeningRoundView(Guid Id, int Number, string Status, long Revision, Guid PresentationTaskId, Guid? ReviewTaskId, Guid? FilingTaskId,
    Guid PresentedDocumentId, Guid? RevisedDocumentId, Guid? SignedDocumentId, DateTime ReceivedAt, DateTime? PresentedAt, DateTime? RespondedAt,
    decimal? ResponseSeconds, string? GeneralObservations, bool AffectedDeclared, bool CanPresent, bool CanRespond, bool CanReview, IReadOnlyList<OpeningRow> Structures);
public sealed record OpeningDocumentView(Guid RoundId, string Kind, int Version, string Sha256, DocumentResponse Document, IReadOnlyList<Guid> AffectedStructureIds);
public sealed record OpeningEventView(Guid RoundId, string Action, DateTime At, string? ActorName, long Revision);
public sealed record OpeningState(Guid ProjectId, Guid SubmissionId, DateTime ServerNow, IReadOnlyList<MeasurementStructureView> Structures,
    IReadOnlyList<OpeningRoundView> Rounds, IReadOnlyList<OpeningDocumentView> Documents, IReadOnlyList<OpeningEventView> History);
public interface IOpeningRecordStore
{
    Task<OpeningState?> ReadAsync(Guid projectId, ProjectReadScope scope, CancellationToken token);
    Task CommandAsync(Guid projectId, Guid roundId, OpeningCommand command, DecisionActor actor, SystemRole role, CancellationToken token);
    Task UploadAsync(Guid projectId, Guid roundId, Guid key, long revision, OpeningDocumentKind kind, IAsyncEnumerable<DocumentUpload> files, DecisionActor actor, SystemRole role, CancellationToken token);
    Task<DocumentDownload> DownloadAsync(Guid projectId, Guid documentId, ProjectReadScope scope, CancellationToken token);
    Task InitializeHistoricalAsync(Guid projectId, Guid checklistId, string reason, DecisionActor actor, CancellationToken token);
}
public sealed class OpeningRecordService(IOpeningRecordStore store, ICurrentUser current, IActiveRoleContext active)
{
    private DecisionActor Actor => new(current.Subject, current.Email, current.Name);
    private ProjectReadScope Scope => new(current.Subject, active.Role);
    public void RequireWrite() { if (active.Role is not (SystemRole.Engineering or SystemRole.CommercialAdvisor)) throw new DocumentException(403, "OpeningAccessDenied"); }
    public Task<OpeningState?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, Scope, token);
    public Task CommandAsync(Guid id, Guid roundId, OpeningCommand c, CancellationToken token)
    {
        RequireWrite(); Key(c.RequestKey, c.ExpectedRevision);
        if (c.Action is not ("Present" or "ConfirmStructure" or "RequestChanges" or "DeclareAffected" or "ReviewCheck" or "CloseReview" or "Accept") ||
            c.Observations?.Length > 4000 || c.Changes?.Count > 10000 || c.AffectedStructureIds?.Count > 10000 ||
            c.Changes?.Any(x => x == null || x.StructureId == Guid.Empty || string.IsNullOrWhiteSpace(x.Description) || x.Description.Length > 4000) == true)
            throw new DocumentException(400, "InvalidOpeningCommand");
        var review = c.Action is "DeclareAffected" or "ReviewCheck" or "CloseReview";
        if (active.Role != (review ? SystemRole.Engineering : SystemRole.CommercialAdvisor)) throw new DocumentException(403, "OpeningAccessDenied");
        return store.CommandAsync(id, roundId, c, Actor, active.Role!.Value, token);
    }
    public Task UploadAsync(Guid id, Guid roundId, Guid key, long revision, OpeningDocumentKind kind, IAsyncEnumerable<DocumentUpload> files, CancellationToken token)
    { RequireWrite(); Key(key, revision); if (!Enum.IsDefined(kind)) throw new DocumentException(400, "InvalidOpeningCommand"); if (active.Role != (kind == OpeningDocumentKind.Revised ? SystemRole.Engineering : SystemRole.CommercialAdvisor)) throw new DocumentException(403, "OpeningAccessDenied"); return store.UploadAsync(id, roundId, key, revision, kind, files, Actor, active.Role!.Value, token); }
    public Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, CancellationToken token) => store.DownloadAsync(id, documentId, Scope, token);
    public Task InitializeHistoricalAsync(Guid id, Guid checklistId, string reason, CancellationToken token)
    {
        if (active.Role != SystemRole.Engineering) throw new DocumentException(403, "OpeningAccessDenied");
        if (checklistId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new DocumentException(400, "InvalidOpeningCommand");
        return store.InitializeHistoricalAsync(id, checklistId, reason, Actor, token);
    }
    private static void Key(Guid key, long revision) { if (key == Guid.Empty || revision < 0) throw new DocumentException(400, "InvalidOpeningCommand"); }
}
