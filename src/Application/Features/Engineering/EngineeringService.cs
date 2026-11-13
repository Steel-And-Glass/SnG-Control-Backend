using SnG.Control.Application.Access;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Engineering;

public sealed record EngineeringCheck(Guid RequestKey, long ExpectedRevision, bool Completed);
public sealed record EngineeringClose(Guid RequestKey, long ExpectedRevision, Guid DocumentId);
public sealed record EngineeringHistorical(Guid SubmissionId, string Reason);
public sealed record EngineeringRow(MeasurementStructureView Structure, long Revision, bool Completed, DateTime? ChangedAt, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EngineeringOrigin? Origin = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EngineeringWork? Work = null);
public sealed record EngineeringOrigin(Guid SubmissionId, int Number, int EvidenceVersion);
public sealed record EngineeringDocument(DocumentResponse Document, int Version, string Sha256);
public sealed record EngineeringHistory(string Action, Guid? StructureId, DateTime At, DocumentPerson Actor, long Revision, string? Reason);
public sealed record EngineeringState(Guid ProjectId, Guid? ChecklistId, Guid? SubmissionId, Guid TaskId, string TaskStatus,
    string ResponsibleSubject, bool CanModify, DateTime StartedAt, DateTime? CompletedAt, long Revision, int Total, int Completed, int Pending, decimal Progress,
    IReadOnlyList<EngineeringRow> Structures, IReadOnlyList<EngineeringDocument> Documents, Guid? CurrentDocumentId, IReadOnlyList<EngineeringHistory> History, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? IsIncremental = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MeasurementDelivery>? Deliveries = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EngineeringWorkspaceView? Workspace = null);
public interface IEngineeringStore
{
    Task<EngineeringState?> ReadAsync(Guid id, ProjectReadScope scope, CancellationToken token);
    Task CheckAsync(Guid id, Guid structureId, EngineeringCheck command, DecisionActor actor, CancellationToken token);
    Task CloseAsync(Guid id, EngineeringClose command, DecisionActor actor, CancellationToken token);
    Task UploadAsync(Guid id, Guid key, long revision, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token);
    Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, ProjectReadScope scope, CancellationToken token);
    Task<byte[]?> SketchAsync(Guid id, Guid structureId, Guid versionId, ProjectReadScope scope, CancellationToken token);
    Task InitializeHistoricalAsync(Guid id, EngineeringHistorical command, DecisionActor actor, CancellationToken token);
}
public sealed class EngineeringService(IEngineeringStore store, ICurrentUser current, IActiveRoleContext active, IEngineeringAssigneeIdentity identity)
{
    private DecisionActor Actor => new(current.Subject, current.Email, current.Name);
    private ProjectReadScope Scope => new(current.Subject, active.Role);
    public void RequireWrite()
    {
        if (active.Role != SystemRole.Engineering || string.IsNullOrWhiteSpace(identity.Subject) || identity.Subject != current.Subject)
            throw new DocumentException(403, "EngineeringAccessDenied");
    }
    private static void Key(Guid key, long revision)
    { if (key == Guid.Empty || revision < 0) throw new DocumentException(400, "InvalidEngineeringCommand"); }
    public Task<EngineeringState?> ReadAsync(Guid id, CancellationToken token) => store.ReadAsync(id, Scope, token);
    public Task CheckAsync(Guid id, Guid structureId, EngineeringCheck command, CancellationToken token)
    { if (active.Role != SystemRole.Engineering || !identity.Subjects.Contains(current.Subject, StringComparer.Ordinal)) throw new DocumentException(403, "EngineeringAccessDenied"); Key(command.RequestKey, command.ExpectedRevision); return store.CheckAsync(id, structureId, command, Actor, token); }
    public Task CloseAsync(Guid id, EngineeringClose command, CancellationToken token)
    { RequireWrite(); Key(command.RequestKey, command.ExpectedRevision); if(command.DocumentId == Guid.Empty) throw new DocumentException(400, "InvalidEngineeringCommand"); return store.CloseAsync(id, command, Actor, token); }
    public Task UploadAsync(Guid id, Guid key, long revision, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token)
    { RequireWrite(); Key(key, revision); return store.UploadAsync(id, key, revision, uploads, Actor, token); }
    public Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, CancellationToken token) => store.DownloadAsync(id, documentId, Scope, token);
    public Task<byte[]?> SketchAsync(Guid id, Guid structureId, Guid versionId, CancellationToken token) => store.SketchAsync(id, structureId, versionId, Scope, token);
    public Task InitializeHistoricalAsync(Guid id, EngineeringHistorical command, CancellationToken token)
    {
        RequireWrite();
        if (command.SubmissionId == Guid.Empty || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 2000) throw new DocumentException(400, "InvalidEngineeringCommand");
        return store.InitializeHistoricalAsync(id, command, Actor, token);
    }
}
