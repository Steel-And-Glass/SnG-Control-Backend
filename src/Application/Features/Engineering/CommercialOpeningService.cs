using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Engineering;
public sealed record CommercialOpeningSelection(Guid StructureId,long ExpectedRevision,int EvidenceVersion,string? Description=null);
public sealed record CommercialOpeningCommand(Guid RequestKey,string Action,long ExpectedRevision,Guid? RoundId=null,Guid? ReviewId=null,IReadOnlyList<CommercialOpeningSelection>? Structures=null,Guid? DocumentId=null,bool? Attest=null,bool? Completed=null,string? Observations=null);
public sealed record CommercialOpeningUpload(Guid RequestKey,long ExpectedRevision,string Kind,Guid? ReviewId,IReadOnlyList<CommercialOpeningSelection> Structures);
public sealed record CommercialOpeningResult(Guid Id);
public sealed record CommercialOpeningRowView(MeasurementStructureView Structure,Guid SubmissionId,int SubmissionNumber,int SourceEvidenceVersion,int EvidenceVersion,Guid EvidenceDocumentId,string Approval,Guid? AcceptanceId,long Revision);
public sealed record CommercialOpeningRoundView(Guid Id,int Number,string Status,long Revision,Guid TaskId,Guid DocumentId,string AdvisorSubject,DateTime ReceivedAt,DateTime? PresentedAt,DateTime? FirstResponseAt,DateTime? ResolvedAt,DateTime? SupersededAt,decimal? ElapsedSeconds,decimal? FirstResponseSeconds,decimal? ResolutionSeconds,bool CanPresent,bool CanRespond,IReadOnlyList<CommercialOpeningRowView> Structures);
public sealed record CommercialOpeningReviewRow(Guid StructureId,int EvidenceVersion,bool Requested,bool Changed,bool Completed,long Revision,DateTime? ChangedAt,string? ChangedBySubject);
public sealed record CommercialOpeningReviewView(Guid Id,Guid RoundId,Guid TaskId,string Subject,DateTime CreatedAt,DateTime? CompletedAt,long Revision,bool Declared,Guid? DocumentId,bool CanReview,IReadOnlyList<CommercialOpeningReviewRow> Structures);
public sealed record CommercialOpeningDocumentRow(Guid StructureId,int EvidenceVersion,Guid EvidenceDocumentId);
public sealed record CommercialOpeningDocumentView(Guid? RoundId,Guid? ReviewId,string Kind,int Version,string Sha256,DocumentResponse Document,IReadOnlyList<CommercialOpeningDocumentRow> Structures);
public sealed record CommercialOpeningAcceptanceView(Guid Id,Guid RoundId,Guid SignedDocumentId,Guid PresentedDocumentId,Guid TaskId,DateTime At,string ActorSubject,bool Attested,IReadOnlyList<CommercialOpeningDocumentRow> Structures);
public sealed record CommercialOpeningEventView(Guid RoundId,string Action,DateTime At,string ActorSubject,IReadOnlyList<OpeningChange> Changes,string? Observations);
public sealed record CommercialOpeningActaView(Guid Id,int Number,string EngineerSubject,DateTime ConfirmedAt,DateTime LastActivityAt,bool CanReceive,IReadOnlyList<CommercialOpeningRoundView> Rounds,IReadOnlyList<CommercialOpeningReviewView> Reviews,IReadOnlyList<CommercialOpeningDocumentView> Documents,IReadOnlyList<CommercialOpeningAcceptanceView> Acceptances,IReadOnlyList<CommercialOpeningEventView> History);
public sealed record CommercialOpeningState(Guid ProjectId,DateTime ServerNow,IReadOnlyList<CommercialOpeningActaView> Actas);
public interface ICommercialOpeningStore
{
    Task<CommercialOpeningState?> ReadAsync(Guid id,ProjectReadScope scope,CancellationToken token);
    Task<CommercialOpeningResult> CommandAsync(Guid id,Guid actaId,CommercialOpeningCommand command,DecisionActor actor,SystemRole role,CancellationToken token);
    Task UploadAsync(Guid id,Guid actaId,Guid roundId,CommercialOpeningUpload command,IAsyncEnumerable<DocumentUpload> files,DecisionActor actor,SystemRole role,CancellationToken token);
    Task<DocumentDownload> DownloadAsync(Guid id,Guid actaId,Guid documentId,ProjectReadScope scope,CancellationToken token);
}
public sealed class CommercialOpeningService(ICommercialOpeningStore store,ICurrentUser current,IActiveRoleContext active)
{
    private DecisionActor Actor=>new(current.Subject,current.Email,current.Name);
    public void RequireWrite(){if(active.Role is not (SystemRole.CommercialAdvisor or SystemRole.Engineering))throw new DocumentException(403,"OpeningAccessDenied");}
    public Task<CommercialOpeningState?> ReadAsync(Guid id,CancellationToken token)=>store.ReadAsync(id,new(current.Subject,active.Role),token);
    private static bool Selection(IReadOnlyList<CommercialOpeningSelection>? s,bool empty=false)=>s!=null&&s.Count<=(500)&& (empty||s.Count>0)&&s.All(r=>r!=null&&r.StructureId!=Guid.Empty&&r.ExpectedRevision>=0&&r.EvidenceVersion>0&&(r.Description==null||r.Description.Length<=4000))&&s.Select(r=>r.StructureId).Distinct().Count()==s.Count;
    public Task<CommercialOpeningResult> CommandAsync(Guid id,Guid acta,CommercialOpeningCommand c,CancellationToken token)
    {
        RequireWrite();var review=c.Action is "DeclareChanged" or "ReviewCheck" or "CloseReview";
        if(active.Role!=(review?SystemRole.Engineering:SystemRole.CommercialAdvisor))throw new DocumentException(403,"OpeningAccessDenied");
        var baseValid=c.RequestKey!=Guid.Empty&&c.ExpectedRevision>=0&&(c.Observations==null||c.Observations.Length<=4000);
        var valid=c.Action switch {
            "Receive"=>c.RoundId==null&&c.ReviewId==null&&c.Structures==null&&c.DocumentId==null&&c.Attest==null&&c.Completed==null&&c.Observations==null,
            "Present"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId==null&&c.Structures==null&&c.DocumentId==null&&c.Attest==null&&c.Completed==null&&c.Observations==null,
            "RequestChanges"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId==null&&Selection(c.Structures)&&c.Structures!.All(s=>!string.IsNullOrWhiteSpace(s.Description))&&c.DocumentId==null&&c.Attest==null&&c.Completed==null,
            "Accept"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId==null&&Selection(c.Structures)&&c.Structures!.All(s=>s.Description==null)&&c.DocumentId is {} d&&d!=Guid.Empty&&c.Attest==true&&c.Completed==null&&c.Observations==null,
            "DeclareChanged"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId is {} v&&v!=Guid.Empty&&Selection(c.Structures,true)&&c.Structures!.All(s=>s.Description==null)&&c.DocumentId==null&&c.Attest==null&&c.Completed==null&&c.Observations==null,
            "ReviewCheck"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId is {} v&&v!=Guid.Empty&&Selection(c.Structures)&&c.Structures!.Count==1&&c.Structures[0].Description==null&&c.DocumentId==null&&c.Attest==null&&c.Completed!=null&&c.Observations==null,
            "CloseReview"=>c.RoundId is {} r&&r!=Guid.Empty&&c.ReviewId is {} v&&v!=Guid.Empty&&c.Structures==null&&c.DocumentId is {} d&&d!=Guid.Empty&&c.Attest==null&&c.Completed==null&&c.Observations==null,
            _=>false};
        if(!baseValid||!valid)throw new DocumentException(400,"InvalidOpeningCommand");return store.CommandAsync(id,acta,c,Actor,active.Role!.Value,token);
    }
    public Task UploadAsync(Guid id,Guid acta,Guid round,CommercialOpeningUpload c,IAsyncEnumerable<DocumentUpload> files,CancellationToken token)
    {
        RequireWrite();if(c.Kind is not ("SIGNED" or "REVISED")||c.RequestKey==Guid.Empty||c.ExpectedRevision<0||!Selection(c.Structures)||c.Structures.Any(s=>s.Description!=null)||(c.Kind=="SIGNED"?c.ReviewId!=null:c.ReviewId==null||c.ReviewId==Guid.Empty))throw new DocumentException(400,"InvalidOpeningCommand");
        if(active.Role!=(c.Kind=="SIGNED"?SystemRole.CommercialAdvisor:SystemRole.Engineering))throw new DocumentException(403,"OpeningAccessDenied");return store.UploadAsync(id,acta,round,c,files,Actor,active.Role.Value,token);
    }
    public Task<DocumentDownload> DownloadAsync(Guid id,Guid acta,Guid doc,CancellationToken token)=>store.DownloadAsync(id,acta,doc,new(current.Subject,active.Role),token);
}
