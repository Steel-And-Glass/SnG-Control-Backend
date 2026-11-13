using System.Text;
using System.Text.Json;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Application;
public sealed class EngineeringTests
{
    [Fact]
    public void InitializationPreservesSnapshotOrderAndPhysicalIdentitiesExcludingOnlyFinallyRetired()
    {
        var now = new DateTime(2026,11,5,12,0,0,DateTimeKind.Utc);
        var source = new MeasurementChecklist(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), now, "installer");
        MeasurementStructureView Row(Guid? item, int unit, DateTime? retired = null) => new(Guid.NewGuid(), item, unit, 3, "V1", "System", "Room", 2, 3, null, now, now, retired, 2, 3, "Actual");
        var rows = new[] { Row(Guid.NewGuid(),1), Row(Guid.NewGuid(),2,now), Row(null,1), Row(Guid.NewGuid(),3) };
        var snapshot = new MeasurementSnapshot(source.CommercialQuotationId, 1, new(3,1,1,3,3,0,100), rows, []);
        source.Submit(now,"installer",JsonSerializer.Serialize(snapshot),Guid.NewGuid());
        var actor = new DecisionActor("installer",null,"Installer");
        Assert.Equal("EngineeringAssigneeNotConfigured", Assert.Throws<DocumentException>(()=>EfEngineeringStore.Create(source,snapshot,"",now,actor)).Code);
        var result=EfEngineeringStore.Create(source,snapshot,"engineer",now,actor);
        var checks=result.Entities.OfType<EngineeringStructure>().ToArray();
        Assert.Equal(rows.Where(r=>r.RetiredAt==null).Select(r=>r.Id), checks.Select(r=>r.StructureId));
        Assert.Equal(new[]{0,1,2},checks.Select(r=>r.Position));
        Assert.All(checks,r=>{Assert.False(r.Completed);Assert.Equal(source.ProjectId,r.ProjectId);});
        Assert.Equal(ProjectTaskType.PrepareOpeningRecord,result.Task.Type);Assert.Equal(OperationalArea.Engineering,result.Task.Area);
        Assert.Null(result.Task.DeadlineAt);Assert.Null(result.Task.QuotationRound);Assert.Equal(now,result.Task.StartedAt);
        var checklist=Assert.Single(result.Entities.OfType<EngineeringChecklist>());
        Assert.Equal(source.SubmissionId,checklist.SubmissionId);
        checklist.SetDocument(Guid.NewGuid());checklist.Complete(now,"engineer","{}");
        Assert.Throws<InvalidOperationException>(()=>checklist.Advance());
        Assert.Throws<InvalidOperationException>(()=>checklist.SetDocument(Guid.NewGuid()));
    }
    [Theory]
    [InlineData("acta.pdf","<html>not a pdf</html>")]
    [InlineData("acta.pdf","%PDF-1.7 truncated")]
    [InlineData("acta.pdf","%PDF-1.7 fake content startxref 0 %%EOF")]
    [InlineData("acta.pdf","%PDF-1.7 fake content startxref 99999 %%EOF")]
    [InlineData("acta.html","%PDF-1.7 example object startxref 0 %%EOF")]
    [InlineData("../acta.pdf","%PDF-1.7 example object startxref 0 %%EOF")]
    public void RejectsWrongTypeTruncationAndUnsafeNames(string name,string content)
        => Assert.Equal("InvalidEngineeringPdf",Assert.Throws<DocumentException>(()=>EfEngineeringStore.ValidatePdf(name,Encoding.ASCII.GetBytes(content))).Code);
}
