using System.Text.Json;
using System.Text.Json.Serialization;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Application.Features.Operations;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewEvidence(Guid? DocumentId,Guid? AttachmentId,string Locator,string Observation);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionReviewCommand(Guid RequestKey,long ExpectedRevision,string Action,string Reason,
    Guid? ReviewId=null,Guid? ExtractionJobId=null,Guid? RowId=null,ReviewedComponent? Values=null,
    Guid? StructureId=null,string? Op=null,Guid? SourceDocumentId=null,Guid? IssueId=null,string? ConflictCode=null,ReviewEvidence? Evidence=null);
public sealed record ReviewCommandResult(Guid ReviewId,long Revision,Guid? ApprovalId=null,Guid? ExtractionJobId=null);
public sealed record ReviewOrigin(Guid ProjectId,Guid FilingId,Guid ReleaseVersionId,string ManifestSha256,string ExtractorVersion);
public sealed record ReviewOpGroup(string Op,Guid StructureId,IReadOnlyList<Guid> RowIds);
public sealed record ReviewView(JsonElement Review,IReadOnlyList<JsonElement> Rows,IReadOnlyList<JsonElement> Issues,JsonElement? Approval,ReviewOrigin Origin,IReadOnlyList<JsonElement> Attachments)
{
    public IReadOnlyList<ReviewOpGroup> Groups=>Rows.GroupBy(r=>new{Op=r.GetProperty("op").GetString()!,Structure=r.GetProperty("structureId").GetGuid()})
        .Select(g=>new ReviewOpGroup(g.Key.Op,g.Key.Structure,g.Select(r=>r.GetProperty("id").GetGuid()).ToArray())).ToArray();
}
public sealed record ReviewDocument(byte[] Bytes,string Name,string ContentType);
public interface IProductionReviewStore
{
    Task<IReadOnlyList<JsonElement>> ListAsync(Guid prep,CancellationToken token);
    Task<ReviewView?> ReadAsync(Guid prep,Guid review,CancellationToken token);
    Task<IReadOnlyList<JsonElement>> HistoryAsync(Guid prep,int skip,CancellationToken token);
    Task<ReviewCommandResult> CommandAsync(Guid prep,ProductionReviewCommand command,string actor,CancellationToken token);
    Task<JsonElement> UploadAsync(Guid prep,Guid request,string name,string? mime,byte[] bytes,string actor,CancellationToken token);
    Task<ReviewDocument> DocumentAsync(Guid prep,Guid document,bool attachment,CancellationToken token);
}
public sealed class ProductionReviewAccess(ProductionPreparationService operations,IActiveRoleContext active,ICurrentUser current,IManagementIdentity management)
{
    public void Read(){if(active.Role==SystemRole.Management&&current.Subject==management.Subject)return;operations.RequireRead();}
    public void Write()=>operations.RequireWrite();
}
public static class ReviewDiagnosticPolicy
{
    public static string Category(string code)=>code switch
    {
        "MATERIAL_UNVERIFIED" or "DIMENSION_UNIT_NOT_EXPLICIT" or "DESCRIPTION_NOT_PROVIDED" or "MATERIAL_ANNOTATION_REQUIRES_REVIEW" or "DUPLICATE_READ" or "DUPLICATE_PIECE_ID"=>"HumanReview",
        "TOTAL_MISMATCH" or "SOURCE_DIMENSION_CONFLICT" or "DOCUMENT_UNREADABLE_OR_LIMIT" or "INCOMPLETE_ROW_WEIGHTS" or "UNCLASSIFIED_LINE" or "NO_COMPONENTS" or "MISSING_PROFILE_CONTEXT" or "MISSING_GLASS_SPECIFICATION" or "PAGE_COVERAGE_INVALID" or "MISSING_OP_COMPONENTS"=>"FormalDocumentResolution",
        _=>"IntegrityOrScope"
    };
    public static void Validate(ReviewedComponent value,bool complete)
    {
        if(value.Material is {} m&&!Enum.IsDefined(m)||value.Route is {} r&&!Enum.IsDefined(r)||value.Quantity is <1 or >100000||value.Measure is <=0||value.CutDimension is <=0||value.Width is <=0||value.Height is <=0||value.LeftAngle is <0 or >180||value.RightAngle is <0 or >180||value.Reference?.Length>200||value.Description?.Length>4000||value.Unit?.Length>30)throw new DocumentException(400,"ReviewValuesInvalid");
        var route=value.Material switch {ComponentMaterial.Glass=>FabricationRoute.Glass,ComponentMaterial.AluminumProfile or ComponentMaterial.MicroperforatedSheet=>FabricationRoute.Aluminum,_=>(FabricationRoute?)null};
        if(value.Route!=route)throw new DocumentException(400,"ReviewMaterialRouteMismatch");
        if(complete&&(value.Material==null||value.Quantity==null||string.IsNullOrWhiteSpace(value.Description)||string.IsNullOrWhiteSpace(value.Unit)||
            (value.Material==ComponentMaterial.AluminumProfile?(string.IsNullOrWhiteSpace(value.Reference)||value.Measure==null||value.CutDimension==null||value.LeftAngle==null||value.RightAngle==null):(value.Width==null||value.Height==null))))throw new DocumentException(409,"ReviewIncomplete");
    }
}
