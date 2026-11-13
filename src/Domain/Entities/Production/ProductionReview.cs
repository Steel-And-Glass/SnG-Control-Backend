using System.Text.Json;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Domain.Entities.Production;
public enum ProductionReviewState { Draft=1, Approved=2, Superseded=3 }
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewedComponent(ComponentMaterial? Material,FabricationRoute? Route,string? Reference,string? Description,
    decimal? Measure,decimal? CutDimension,decimal? Width,decimal? Height,string? Unit,decimal? LeftAngle,decimal? RightAngle,int? Quantity);
public sealed class ProductionReview
{
    private ProductionReview() { }
    public ProductionReview(Guid prep,Guid job,Guid source,int number,DateTime at,string actor)
    {Id=Guid.NewGuid();PreparationId=prep;ExtractionJobId=job;SourceRevisionId=source;Number=number;CreatedAt=at;CreatedBySubject=actor;}
    public Guid Id {get;private set;} public Guid PreparationId {get;private set;} public Guid ExtractionJobId {get;private set;}
    public Guid SourceRevisionId {get;private set;} public int Number {get;private set;} public long Revision {get;private set;}
    public long DataRevision {get;private set;} public DateTime CreatedAt {get;private set;} public string CreatedBySubject {get;private set;}="";
    public ProductionReviewState State {get;private set;}=ProductionReviewState.Draft;
    public void Change(bool data=false){if(State!=ProductionReviewState.Draft)throw new InvalidOperationException("Review frozen.");Revision++;if(data)DataRevision++;}
    public void Approve(){Change();State=ProductionReviewState.Approved;}
    public void Supersede(){Change();State=ProductionReviewState.Superseded;}
}
public sealed class ProductionReviewRow
{
    private ProductionReviewRow() { }
    public ProductionReviewRow(ProductionReview review,Guid? candidate,Guid structure,string op,Guid document,string hash,string original,ReviewedComponent values)
    {Id=Guid.NewGuid();ReviewId=review.Id;PreparationId=review.PreparationId;CandidateId=candidate;StructureId=structure;Op=op;DocumentId=document;DocumentSha256=hash;OriginalJson=original;ValuesJson=JsonSerializer.Serialize(values);}
    public Guid Id {get;private set;} public Guid ReviewId {get;private set;} public Guid PreparationId {get;private set;}
    public Guid? CandidateId {get;private set;} public Guid StructureId {get;private set;} public string Op {get;private set;}="";
    public Guid DocumentId {get;private set;} public string DocumentSha256 {get;private set;}="";
    public string OriginalJson {get;private set;}="{}"; public string ValuesJson {get;private set;}="{}";
    public bool Reviewed {get;private set;} public bool Excluded {get;private set;}
    public void Edit(ReviewedComponent values){ValuesJson=JsonSerializer.Serialize(values);Reviewed=true;}
    public void Exclude(bool value){Excluded=value;Reviewed=true;}
    public ReviewedComponent Values()=>JsonSerializer.Deserialize<ReviewedComponent>(ValuesJson)!;
}
public sealed class ProductionReviewIssue
{
    private ProductionReviewIssue() { }
    public ProductionReviewIssue(Guid review,string code,string category,string evidence)
    {Id=Guid.NewGuid();ReviewId=review;Code=code;Category=category;EvidenceJson=evidence;}
    public Guid Id {get;private set;}public Guid ReviewId {get;private set;}public string Code {get;private set;}="";
    public string Category {get;private set;}="";public string EvidenceJson {get;private set;}="{}";
    public long? ResolvedDataRevision {get;private set;}
    public void Resolve(long revision){if(Category!="HumanReview")throw new InvalidOperationException("No authorized resolution policy.");ResolvedDataRevision=revision;}
}
public sealed class ProductionReviewEvent
{
    private ProductionReviewEvent() { }
    public ProductionReviewEvent(Guid prep,Guid review,Guid key,string fingerprint,string action,string reason,DateTime at,string actor,string detail,string result)
    {Id=Guid.NewGuid();PreparationId=prep;ReviewId=review;RequestKey=key;Fingerprint=fingerprint;Action=action;Reason=reason;At=at;Actor=actor;DetailJson=detail;ResultJson=result;}
    public Guid Id {get;private set;}public Guid PreparationId {get;private set;}public Guid ReviewId {get;private set;}public Guid RequestKey {get;private set;}
    public string Fingerprint {get;private set;}="";public string Action {get;private set;}="";public string Reason {get;private set;}="";
    public DateTime At {get;private set;}public string Actor {get;private set;}="";public string DetailJson {get;private set;}="{}";public string ResultJson {get;private set;}="{}";
}
public sealed class ProductionApproval
{
    private ProductionApproval() { }
    public ProductionApproval(ProductionReview review,string canonical,DateTime at,string actor)
    {Id=Guid.NewGuid();ReviewId=review.Id;PreparationId=review.PreparationId;Revision=review.Revision;Canonical=canonical;Sha256=IncrementalEvidence.Hash(canonical);At=at;Actor=actor;}
    public Guid Id {get;private set;}public Guid ReviewId {get;private set;}public Guid PreparationId {get;private set;}
    public long Revision {get;private set;}public string Canonical {get;private set;}="";public string Sha256 {get;private set;}="";
    public DateTime At {get;private set;}public string Actor {get;private set;}="";
}
public sealed class ProductionReviewAttachment
{
    private ProductionReviewAttachment() { }
    public ProductionReviewAttachment(Guid prep,Guid key,string name,string? mime,long size,string hash,DateTime at,string actor)
    {Id=Guid.NewGuid();PreparationId=prep;RequestKey=key;Name=name;ContentType=mime;SizeBytes=size;Sha256=hash;At=at;Actor=actor;}
    public Guid Id {get;private set;}public Guid PreparationId {get;private set;}public Guid RequestKey {get;private set;}
    public string Name {get;private set;}="";public string? ContentType {get;private set;}public long SizeBytes {get;private set;}
    public string Sha256 {get;private set;}="";public DateTime At {get;private set;}public string Actor {get;private set;}="";
    public string Key()=> $"production-review/{PreparationId:D}/evidence/{Id:D}/content";
}
