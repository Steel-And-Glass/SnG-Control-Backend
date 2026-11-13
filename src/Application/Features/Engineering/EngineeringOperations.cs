using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Engineering;
public sealed record EngineeringReference(Guid StructureId, Guid SubmissionId, int EvidenceVersion);
public sealed record EngineeringCommand(Guid RequestKey, long ExpectedRevision, string Action, Guid? TargetId, string? Subject, IReadOnlyList<EngineeringReference>? Structures, Guid? DocumentId);
public sealed record EngineeringCommandResult(Guid TargetId);
public sealed record EngineeringWork(Guid PackageId, string ResponsibleSubject, bool CanModify, string? ChangedBySubject, Guid? OpeningRecordId);
public sealed record EngineeringAssignmentView(Guid Id, string Subject, DateTime AssignedAt, DateTime? EndedAt, IReadOnlyList<EngineeringReference> Structures);
public sealed record EngineeringPackageView(Guid Id, bool IsIntake, string Status, long Revision, string ResponsibleSubject, DateTime CreatedAt, DateTime AssignedAt, DateTime? CompletedAt, IReadOnlyList<EngineeringReference> Structures, IReadOnlyList<EngineeringAssignmentView> History);
public sealed record EngineeringScopedDocument(EngineeringDocument File, IReadOnlyList<EngineeringReference> Structures, bool MatchesManifest);
public sealed record EngineeringActaView(Guid Id, int Number, string Status, string ResponsibleSubject, string CreatedBySubject, DateTime CreatedAt, long Revision, bool CanModify, IReadOnlyList<EngineeringReference> Structures, string ScopeHash, Guid? CurrentDocumentId, IReadOnlyList<EngineeringScopedDocument> Documents, DateTime? ConfirmedAt, string? ConfirmedBySubject);
public sealed record EngineeringWorkspaceHistory(string Action, Guid TargetId, long Revision, DateTime At, string ActorSubject);
public sealed record EngineeringWorkspaceView(string ProjectName, string? SgCode, long Revision, bool CanManagePackages, IReadOnlyList<string> AssignableSubjects, IReadOnlyList<EngineeringPackageView> Packages, IReadOnlyList<EngineeringActaView> Actas, IReadOnlyList<EngineeringWorkspaceHistory> History);
public interface IEngineeringOperationsStore
{
    Task<EngineeringCommandResult> ExecuteAsync(Guid id, EngineeringCommand command, DecisionActor actor, CancellationToken token);
    Task UploadOpeningAsync(Guid id, Guid actaId, Guid key, long revision, string scopeHash, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token);
    Task<DocumentDownload> DownloadOpeningAsync(Guid id, Guid actaId, Guid documentId, ProjectReadScope scope, CancellationToken token);
}
public sealed class EngineeringOperationsService(IEngineeringOperationsStore store, ICurrentUser current, IActiveRoleContext active, IEngineeringAssigneeIdentity identity)
{
    private DecisionActor Actor => new(current.Subject,current.Email,current.Name);
    public void RequireWrite()
    { if(active.Role!=SystemRole.Engineering || !identity.Subjects.Contains(current.Subject,StringComparer.Ordinal)) throw new DocumentException(403,"EngineeringAccessDenied"); }
    public Task<EngineeringCommandResult> ExecuteAsync(Guid id, EngineeringCommand c, CancellationToken token)
    {
        RequireWrite();
        var selection=c.Structures is {Count: >0 and <=500} && c.Structures.All(x=>x.StructureId!=Guid.Empty && x.SubmissionId!=Guid.Empty && x.EvidenceVersion==1) && c.Structures.Select(x=>x.StructureId).Distinct().Count()==c.Structures.Count;
        var target=c.TargetId is {} t && t!=Guid.Empty;
        var valid=c.Action switch {
            "CreatePackage" => selection && c.TargetId==null && c.DocumentId==null && identity.Subjects.Contains(c.Subject ?? "",StringComparer.Ordinal),
            "ReassignPackage" => target && c.Structures==null && c.DocumentId==null && identity.Subjects.Contains(c.Subject ?? "",StringComparer.Ordinal),
            "CreateActa" => selection && c.TargetId==null && identity.Subjects.Contains(c.Subject ?? "",StringComparer.Ordinal) && c.DocumentId==null,
            "AssignActa" => target && c.Structures==null && c.DocumentId==null && identity.Subjects.Contains(c.Subject ?? "",StringComparer.Ordinal),
            "AddStructures" => target && selection && c.Subject==null && c.DocumentId==null,
            "SelectDocument" => target && c.Structures==null && c.Subject==null && c.DocumentId is {} d && d!=Guid.Empty,
            "ConfirmActa" => target && c.Structures==null && c.Subject==null && c.DocumentId==null,
            _ => false };
        if(c.RequestKey==Guid.Empty || c.ExpectedRevision<0 || !valid) throw new DocumentException(400,"InvalidEngineeringCommand");
        if(c.Action is "CreatePackage" or "ReassignPackage" or "CreateActa" or "AssignActa" && current.Subject!=identity.Subject) throw new DocumentException(403,"EngineeringAccessDenied");
        return store.ExecuteAsync(id,c,Actor,token);
    }
    public Task UploadAsync(Guid id,Guid actaId,Guid key,long revision,string scopeHash,IAsyncEnumerable<DocumentUpload> uploads,CancellationToken token)
    { RequireWrite(); if(key==Guid.Empty || revision<0 || scopeHash.Length!=64 || scopeHash.Any(c=>!char.IsAsciiHexDigit(c))) throw new DocumentException(400,"InvalidEngineeringCommand"); return store.UploadOpeningAsync(id,actaId,key,revision,scopeHash,uploads,Actor,token); }
    public Task<DocumentDownload> DownloadAsync(Guid id,Guid actaId,Guid documentId,CancellationToken token)=>store.DownloadOpeningAsync(id,actaId,documentId,new(current.Subject,active.Role),token);
}
