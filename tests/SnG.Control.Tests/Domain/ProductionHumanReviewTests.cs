using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Tests.Domain;
public class ProductionHumanReviewTests
{
    private static ReviewedComponent Complete=>new(ComponentMaterial.Glass,FabricationRoute.Glass,null,"Explicit glass specification",null,null,975,384,"mm",null,null,2);
    [Theory][InlineData("TOTAL_MISMATCH","FormalDocumentResolution")][InlineData("SOURCE_DIMENSION_CONFLICT","FormalDocumentResolution")][InlineData("DOCUMENT_UNREADABLE_OR_LIMIT","FormalDocumentResolution")][InlineData("UNCLASSIFIED_LINE","FormalDocumentResolution")][InlineData("UNKNOWN_OP","IntegrityOrScope")][InlineData("DocumentHashInvalid","IntegrityOrScope")][InlineData("MATERIAL_UNVERIFIED","HumanReview")][InlineData("DUPLICATE_READ","HumanReview")][InlineData("DUPLICATE_PIECE_ID","HumanReview")][InlineData("MATERIAL_ANNOTATION_REQUIRES_REVIEW","HumanReview")]
    public void ExplicitDiagnosticPolicies(string code,string category)=>Assert.Equal(category,ReviewDiagnosticPolicy.Category(code));
    [Fact]public void MaterialAndRouteAreSeparateAndMicroperforatedUsesAluminum()
    {
        ReviewDiagnosticPolicy.Validate(Complete,true);
        Assert.Throws<DocumentException>(()=>ReviewDiagnosticPolicy.Validate(Complete with{Material=ComponentMaterial.MicroperforatedSheet},true));
        ReviewDiagnosticPolicy.Validate(Complete with{Material=ComponentMaterial.MicroperforatedSheet,Route=FabricationRoute.Aluminum},true);
        Assert.Throws<DocumentException>(()=>ReviewDiagnosticPolicy.Validate(Complete with{Quantity=0},true));
        Assert.Throws<DocumentException>(()=>ReviewDiagnosticPolicy.Validate(Complete with{Material=null,Route=null},true));
        Assert.Throws<DocumentException>(()=>ReviewDiagnosticPolicy.Validate(Complete with{Unit=null},true));
        Assert.Equal(975,Complete.Width);Assert.Equal(384,Complete.Height);
    }
    [Fact]public void FormalDifferencesAndIntegrityCannotBeResolvedByAttestation()
    {
        foreach(var code in new[]{"TOTAL_MISMATCH","SOURCE_DIMENSION_CONFLICT","UNKNOWN_OP","DocumentHashInvalid"})
        {
            var issue=new ProductionReviewIssue(Guid.NewGuid(),code,ReviewDiagnosticPolicy.Category(code),"{\"base\":28,\"split\":35}");
            Assert.Throws<InvalidOperationException>(()=>issue.Resolve(1));Assert.Null(issue.ResolvedDataRevision);Assert.Contains("28",issue.EvidenceJson);Assert.Contains("35",issue.EvidenceJson);
        }
    }
    [Fact]public void FrozenReviewCannotChangeAndCorrectionsInvalidateWarningResolution()
    {
        var review=new ProductionReview(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,DateTime.UtcNow,"synthetic");
        var warning=new ProductionReviewIssue(review.Id,"MATERIAL_UNVERIFIED","HumanReview","{}");warning.Resolve(review.DataRevision);
        review.Change(true);Assert.NotEqual(review.DataRevision,warning.ResolvedDataRevision);review.Approve();
        Assert.Throws<InvalidOperationException>(()=>review.Change());Assert.Throws<InvalidOperationException>(()=>review.Approve());
    }
}
