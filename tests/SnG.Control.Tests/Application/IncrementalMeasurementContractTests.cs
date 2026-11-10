using SnG.Control.Application.Features.Measurements;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Tests.Application;
public sealed class IncrementalMeasurementContractTests
{
    [Fact]
    public void ActivationIsExplicitAndRestrictedToNewProjects()
    {
        var cutoff = new DateTime(2026,11,15,0,0,0,DateTimeKind.Utc);
        Assert.False(new IncrementalMeasurementActivation().Includes(cutoff));
        Assert.False(new IncrementalMeasurementActivation(cutoff).Includes(cutoff.AddTicks(-1)));
        Assert.True(new IncrementalMeasurementActivation(cutoff).Includes(cutoff));
        Assert.False(new IncrementalMeasurementActivation(DateTime.SpecifyKind(cutoff, DateTimeKind.Unspecified)).Includes(cutoff));
    }
    [Theory]
    [InlineData("empty")][InlineData("duplicate")][InlineData("oversized")][InlineData("foreignCoverage")][InlineData("emptyCoverage")][InlineData("duplicateDocument")][InlineData("emptyKey")][InlineData("revision")]
    public void RejectsAmbiguousOrUnboundedSubmissionCommands(string kind)
    {
        var id=Guid.NewGuid(); var doc=Guid.NewGuid();
        var command=new MeasurementSubmissionCommand(Guid.NewGuid(),0,[id],[new(doc,[id])]);
        command=kind switch {
            "empty" => command with {StructureIds=[]}, "duplicate" => command with {StructureIds=[id,id]},
            "oversized" => command with {StructureIds=Enumerable.Range(0,501).Select(_=>Guid.NewGuid()).ToArray()},
            "foreignCoverage" => command with {Documents=[new(doc,[Guid.NewGuid()])]}, "emptyCoverage" => command with {Documents=[new(doc,[])]},
            "duplicateDocument" => command with {Documents=[new(doc,[id]),new(doc,[id])]}, "emptyKey" => command with {RequestKey=Guid.Empty},
            _ => command with {ExpectedRevision=-1}};
        Assert.Equal(400,Assert.Throws<DocumentException>(command.Validate).Status);
    }
    [Fact]
    public void CompleteChecksPreserveIdentityAndAdvanceOnlyTheirOwnRevision()
    {
        var check=new EngineeringEvidenceCheck(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,false,0,null,null,null);
        var id=check.StructureId; var at=DateTime.UnixEpoch;
        check.SetCompleted(true,at,"synthetic-engineer"); check.SetCompleted(false,at.AddSeconds(1),"synthetic-engineer");
        Assert.Equal(id,check.StructureId);Assert.False(check.Completed);Assert.Equal(2,check.Revision);Assert.Equal("synthetic-engineer",check.ChangedBySubject);
        var audit=new EngineeringCheckEvent(check,Guid.NewGuid(),new string('A',64),null,null);
        Assert.Equal(check.Revision,audit.Revision);Assert.Equal(check.ChangedAt,audit.At);
    }
}
