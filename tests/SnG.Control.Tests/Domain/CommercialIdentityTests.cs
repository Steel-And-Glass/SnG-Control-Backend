using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Domain;

public class CommercialIdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void NewProject_RejectsMissingSubjectEvenWithEmail(string? subject)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Project(Guid.NewGuid(), "Project", "City", ProductLine.Classic,
            subject!, "test@example.com", "Test User"));
        Assert.Single(typeof(Project).GetConstructors());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    [InlineData("a@example.com", "Name")]
    public void Creator_PreservesOpaqueSubjectAndOptionalSnapshotsAcrossChanges(string? email, string? name)
    {
        var project = new Project(Guid.NewGuid(), "Project", "City", ProductLine.Classic, "  Opaque-SUB-001  ", email, name);
        project.SetSgCode("SG001");
        Assert.Equal("  Opaque-SUB-001  ", project.CreatedByGoogleSubject);
        Assert.Equal(string.IsNullOrWhiteSpace(email) ? null : email, project.CreatedByEmail);
        Assert.Equal(string.IsNullOrWhiteSpace(name) ? null : name, project.CreatedByName);
    }

    [Fact]
    public void Task_PreservesPersonalAssignmentAndAllowsAreaOnlyOrHistoricalNull()
    {
        var prepare = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        var legacy = new ProjectTask(Guid.NewGuid(), OperationalArea.Commercial, ProjectTaskType.ReviewQuotation);
        var review = new ProjectTask(Guid.NewGuid(), OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, " Opaque-SUB ");
        review.Start(); review.Complete();
        Assert.Null(prepare.AssignedToGoogleSubject); Assert.Null(legacy.AssignedToGoogleSubject);
        Assert.Equal(" Opaque-SUB ", review.AssignedToGoogleSubject);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void Task_RejectsBlankPersonalAssignment(string subject) => Assert.ThrowsAny<ArgumentException>(() =>
        new ProjectTask(Guid.NewGuid(), OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, subject));
}
