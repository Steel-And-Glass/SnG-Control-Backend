namespace SnG.Control.Domain.Entities.Incremental;

// Native rounds deliberately do not reuse the historical global-round state machine.
public sealed class CommercialOpeningRound
{
    private CommercialOpeningRound() { }
    public CommercialOpeningRound(Guid id, Guid projectId, Guid actaId, int number, ProjectTask task, Guid documentId, DateTime at)
    {
        MeasurementChecklist.ValidateActor(at, task.AssignedToGoogleSubject!);
        if(id==Guid.Empty || actaId==Guid.Empty || number<1 || task.ProjectId!=projectId || documentId==Guid.Empty) throw new ArgumentException("Invalid round.");
        Id=id;ProjectId=projectId;OpeningRecordId=actaId;Number=number;TaskId=task.Id;DocumentId=documentId;ReceivedAt=at;LastActivityAt=at;AdvisorSubject=task.AssignedToGoogleSubject!;
    }
    public Guid Id {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public int Number {get;private set;}
    public Guid TaskId {get;private set;}
    public Guid DocumentId {get;private set;}
    public string AdvisorSubject {get;private set;}="";
    public DateTime ReceivedAt {get;private set;}
    public DateTime LastActivityAt {get;private set;}
    public DateTime? PresentedAt {get;private set;}
    public DateTime? FirstResponseAt {get;private set;}
    public DateTime? ResolvedAt {get;private set;}
    public DateTime? SupersededAt {get;private set;}
    public long Revision {get;private set;}
    public void Advance(DateTime at){if(SupersededAt!=null || at.Kind!=DateTimeKind.Utc || at<LastActivityAt)throw new InvalidOperationException("OpeningRoundStateConflict");Revision=checked(Revision+1);LastActivityAt=at;}
    public void Present(DateTime at){if(PresentedAt!=null || ResolvedAt!=null)throw new InvalidOperationException("OpeningRoundStateConflict");Advance(at);PresentedAt=at;}
    public void Respond(DateTime at){if(PresentedAt==null || ResolvedAt!=null)throw new InvalidOperationException("OpeningRoundStateConflict");Advance(at);FirstResponseAt??=at;}
    public void Resolve(DateTime at){if(PresentedAt==null)throw new InvalidOperationException("OpeningRoundStateConflict");Advance(at);ResolvedAt=at;}
    public void Supersede(DateTime at){Advance(at);SupersededAt=at;}
}
public sealed class CommercialOpeningRow
{
    private CommercialOpeningRow() { }
    public CommercialOpeningRow(CommercialOpeningRound round, OpeningRecordMember member, CommercialOpeningRow? previous=null, bool changed=false, bool reviewed=false)
    {
        if(member.ProjectId!=round.ProjectId || member.OpeningRecordId!=round.OpeningRecordId)throw new ArgumentException("Foreign evidence.");
        RoundId=round.Id;ProjectId=round.ProjectId;OpeningRecordId=round.OpeningRecordId;StructureId=member.StructureId;
        EvidenceVersion=checked((previous?.EvidenceVersion??1)+(changed?1:0));EvidenceDocumentId=previous==null||changed?round.DocumentId:previous.EvidenceDocumentId;
        if(previous!=null && !changed && !reviewed){Approval=previous.Approval;AcceptanceId=previous.AcceptanceId;}
        if(changed && previous?.AcceptanceId!=null)throw new InvalidOperationException("OpeningReleasedEvidenceLocked");
    }
    public Guid RoundId {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid StructureId {get;private set;}
    public int EvidenceVersion {get;private set;}
    public Guid EvidenceDocumentId {get;private set;}
    public string Approval {get;private set;}="AwaitingApproval";
    public Guid? AcceptanceId {get;private set;}
    public long Revision {get;private set;}
    public void Review(){if(Approval is not ("AwaitingApproval" or "UnderEngineeringReview")||AcceptanceId!=null)throw new InvalidOperationException("OpeningApprovalLocked");Approval="UnderEngineeringReview";Revision=checked(Revision+1);}
    public void Accept(Guid acceptance){if(Approval!="AwaitingApproval"||acceptance==Guid.Empty||AcceptanceId!=null)throw new InvalidOperationException("OpeningApprovalLocked");Approval="Confirmed";AcceptanceId=acceptance;Revision=checked(Revision+1);}
}
public sealed class CommercialOpeningReview
{
    private CommercialOpeningReview() { }
    public CommercialOpeningReview(CommercialOpeningRound round,ProjectTask task,DateTime at)
    {Id=Guid.NewGuid();ProjectId=round.ProjectId;OpeningRecordId=round.OpeningRecordId;RoundId=round.Id;TaskId=task.Id;Subject=task.AssignedToGoogleSubject!;CreatedAt=at;}
    public Guid Id {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid RoundId {get;private set;}
    public Guid TaskId {get;private set;}
    public string Subject {get;private set;}="";
    public DateTime CreatedAt {get;private set;}
    public DateTime? CompletedAt {get;private set;}
    public long Revision {get;private set;}
    public bool Declared {get;private set;}
    public Guid? DocumentId {get;private set;}
    public void Advance(){if(CompletedAt!=null)throw new InvalidOperationException("OpeningReviewClosed");Revision=checked(Revision+1);}
    public void Invalidate(){Advance();Declared=false;DocumentId=null;}
    public void Declare(){Advance();Declared=true;DocumentId=null;}
    public void SetDocument(Guid id){if(!Declared||id==Guid.Empty)throw new InvalidOperationException("OpeningAffectedStructuresRequired");Advance();DocumentId=id;}
    public void Complete(DateTime at){if(!Declared||DocumentId==null||at.Kind!=DateTimeKind.Utc||at<CreatedAt)throw new InvalidOperationException("OpeningReviewIncomplete");Advance();CompletedAt=at;}
}
public sealed class CommercialOpeningReviewMember
{
    private CommercialOpeningReviewMember() { }
    public CommercialOpeningReviewMember(CommercialOpeningReview review,CommercialOpeningRow row,bool requested)
    {if(review.ProjectId!=row.ProjectId||review.RoundId!=row.RoundId)throw new ArgumentException("Foreign review.");ReviewId=review.Id;RoundId=row.RoundId;ProjectId=row.ProjectId;OpeningRecordId=row.OpeningRecordId;StructureId=row.StructureId;EvidenceVersion=row.EvidenceVersion;Requested=requested;}
    public Guid ReviewId {get;private set;}
    public Guid RoundId {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid StructureId {get;private set;}
    public int EvidenceVersion {get;private set;}
    public bool Requested {get;private set;}
    public bool Changed {get;private set;}
    public bool Completed {get;private set;}
    public long Revision {get;private set;}
    public DateTime? ChangedAt {get;private set;}
    public string? ChangedBySubject {get;private set;}
    public void Request(DateTime at,string subject){Requested=true;Check(false,at,subject);}
    public void Declare(bool changed){if(Changed!=changed){Changed=changed;Revision=checked(Revision+1);}}
    public void Check(bool completed,DateTime at,string subject){MeasurementChecklist.ValidateActor(at,subject);Completed=completed;ChangedAt=at;ChangedBySubject=subject;Revision=checked(Revision+1);}
}
public sealed class CommercialOpeningDocument
{
    private CommercialOpeningDocument() { }
    public CommercialOpeningDocument(Guid project,Guid acta,Guid round,Guid document,string kind,Guid? review,string canonical,string hash)
    {IncrementalEvidence.Json(canonical);IncrementalEvidence.HashValue(hash);ProjectId=project;OpeningRecordId=acta;RoundId=round;DocumentId=document;Kind=kind;ReviewId=review;ScopeCanonical=canonical;ScopeHash=hash;}
    public Guid DocumentId {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid RoundId {get;private set;}
    public string Kind {get;private set;}="";
    public Guid? ReviewId {get;private set;}
    public string ScopeCanonical {get;private set;}="";
    public string ScopeHash {get;private set;}="";
}
public sealed class CommercialOpeningDocumentMember
{
    private CommercialOpeningDocumentMember() { }
    public CommercialOpeningDocumentMember(CommercialOpeningDocument doc,CommercialOpeningRow row,int evidenceVersion)
    {DocumentId=doc.DocumentId;RoundId=doc.RoundId;ProjectId=doc.ProjectId;OpeningRecordId=doc.OpeningRecordId;StructureId=row.StructureId;EvidenceVersion=evidenceVersion;EvidenceDocumentId=doc.Kind=="REVISED"&&evidenceVersion>row.EvidenceVersion?doc.DocumentId:row.EvidenceDocumentId;}
    public Guid DocumentId {get;private set;}
    public Guid RoundId {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid StructureId {get;private set;}
    public int EvidenceVersion {get;private set;}
    public Guid EvidenceDocumentId {get;private set;}
}
public sealed class CommercialOpeningEvent
{
    private CommercialOpeningEvent() { }
    public CommercialOpeningEvent(Guid project,Guid acta,Guid round,Guid key,string fingerprint,string action,Guid result,DateTime at,string subject,string canonical)
    {MeasurementChecklist.ValidateActor(at,subject);IncrementalEvidence.HashValue(fingerprint);IncrementalEvidence.Json(canonical);ProjectId=project;OpeningRecordId=acta;RoundId=round;RequestKey=key;Fingerprint=fingerprint;Action=action;ResultId=result;At=at;ActorSubject=subject;Canonical=canonical;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid RoundId {get;private set;}
    public Guid RequestKey {get;private set;}
    public string Fingerprint {get;private set;}="";
    public string Action {get;private set;}="";
    public Guid ResultId {get;private set;}
    public DateTime At {get;private set;}
    public string ActorSubject {get;private set;}="";
    public string Canonical {get;private set;}="";
}
public sealed class CommercialOpeningAcceptanceEvidence
{
    private CommercialOpeningAcceptanceEvidence() { }
    public CommercialOpeningAcceptanceEvidence(OpeningAcceptance acceptance,CommercialOpeningDocumentMember signed)
    {if(acceptance.ProjectId!=signed.ProjectId||acceptance.RoundId!=signed.RoundId||acceptance.SignedDocumentId!=signed.DocumentId)throw new ArgumentException("Foreign signed scope.");AcceptanceId=acceptance.Id;ProjectId=acceptance.ProjectId;OpeningRecordId=acceptance.OpeningRecordId;RoundId=signed.RoundId;StructureId=signed.StructureId;EvidenceVersion=signed.EvidenceVersion;EvidenceDocumentId=signed.EvidenceDocumentId;SignedDocumentId=signed.DocumentId;Attested=true;}
    public Guid AcceptanceId {get;private set;}
    public Guid ProjectId {get;private set;}
    public Guid OpeningRecordId {get;private set;}
    public Guid RoundId {get;private set;}
    public Guid StructureId {get;private set;}
    public int EvidenceVersion {get;private set;}
    public Guid EvidenceDocumentId {get;private set;}
    public Guid SignedDocumentId {get;private set;}
    public bool Attested {get;private set;}
}
