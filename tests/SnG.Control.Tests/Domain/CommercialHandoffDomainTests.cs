using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Domain;

public class CommercialHandoffDomainTests
{
    [Theory]
    [InlineData("StartedAt")]
    [InlineData("CompletedAt")]
    public void Complete_RejectsInconsistentDates(string property)
    {
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation); task.Start();
        typeof(ProjectTask).GetProperty(property)!.SetValue(task, property == "StartedAt" ? null : DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(task.Complete); Assert.Equal(ProjectTaskStatus.InProgress, task.Status);
    }
    [Fact]
    public void Complete_RequiresStartedTaskAndPreservesDates()
    {
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        Assert.Throws<InvalidOperationException>(task.Complete);
        task.Start(); var assigned = task.AssignedAt; var started = task.StartedAt;
        task.Complete();
        Assert.Equal(ProjectTaskStatus.Completed, task.Status);
        Assert.Equal(DateTimeKind.Utc, task.CompletedAt!.Value.Kind);
        Assert.Equal(assigned, task.AssignedAt); Assert.Equal(started, task.StartedAt);
        var completed = task.CompletedAt;
        Assert.Throws<InvalidOperationException>(task.Complete); Assert.Equal(completed, task.CompletedAt);
    }
    [Fact]
    public void ReviewQuotation_HasStableTypeAndStartsImmediately()
    {
        Assert.Equal(1, (int)ProjectTaskType.PrepareQuotation); Assert.Equal(2, (int)ProjectTaskType.ReviewQuotation);
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Commercial, ProjectTaskType.ReviewQuotation);
        task.Start(); Assert.Equal(ProjectTaskStatus.InProgress, task.Status); Assert.NotNull(task.StartedAt); Assert.Null(task.CompletedAt);
    }
    [Fact]
    public async Task ConfirmedKey_IsInitiallyNullAndAssignedOnlyOnce()
    {
        var version = (await CommercialHandoffScenario.CreateAsync()).State!.Version!;
        Assert.Null(version.ConfirmedFileStorageKey); version.SetConfirmedFileStorageKey("projects/a/confirmed.xlsx");
        Assert.Equal("projects/a/confirmed.xlsx", version.ConfirmedFileStorageKey);
        Assert.Throws<InvalidOperationException>(() => version.SetConfirmedFileStorageKey("projects/b/confirmed.xlsx"));
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/absolute.xlsx")]
    [InlineData("C:/absolute.xlsx")]
    [InlineData("../confirmed.xlsx")]
    [InlineData("a/../confirmed.xlsx")]
    [InlineData("a\\confirmed.xlsx")]
    [InlineData("a//confirmed.xlsx")]
    public async Task ConfirmedKey_RejectsUnsafePaths(string key)
    {
        var version = (await CommercialHandoffScenario.CreateAsync()).State!.Version!;
        Assert.ThrowsAny<ArgumentException>(() => version.SetConfirmedFileStorageKey(key)); Assert.Null(version.ConfirmedFileStorageKey);
    }
}
