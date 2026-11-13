using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Domain;

public class ProjectTaskTests
{
    [Fact]
    public void Start_PendingTask_SetsUtcStartWithoutChangingAssignment()
    {
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        var assignment = (task.Id, task.ProjectId, task.Area, task.Type, task.AssignedAt);
        var before = DateTime.UtcNow;
        task.Start();
        Assert.Equal(ProjectTaskStatus.InProgress, task.Status);
        Assert.NotNull(task.StartedAt);
        Assert.Equal(DateTimeKind.Utc, task.StartedAt.Value.Kind);
        Assert.InRange(task.StartedAt.Value, before, DateTime.UtcNow);
        Assert.Null(task.CompletedAt);
        Assert.Equal(assignment, (task.Id, task.ProjectId, task.Area, task.Type, task.AssignedAt));
    }

    [Theory]
    [InlineData(ProjectTaskStatus.InProgress)]
    [InlineData(ProjectTaskStatus.Completed)]
    public void Start_NonPendingTask_RejectsWithoutChangingDates(ProjectTaskStatus status)
    {
        var task = new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        task.Start();
        if (status == ProjectTaskStatus.Completed)
            typeof(ProjectTask).GetProperty(nameof(ProjectTask.Status))!.SetValue(task, status);
        var started = task.StartedAt;
        Assert.Throws<InvalidOperationException>(task.Start);
        Assert.Equal(status, task.Status);
        Assert.Equal(started, task.StartedAt);
        Assert.Null(task.CompletedAt);
    }

    [Theory]
    [InlineData(OperationalArea.Commercial)]
    [InlineData(OperationalArea.Budgets)]
    public void Constructor_CreatesNewPendingAssignment(OperationalArea area)
    {
        var projectId = Guid.NewGuid();
        var before = DateTime.UtcNow;
        var task = new ProjectTask(projectId, area, ProjectTaskType.PrepareQuotation);
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(projectId, task.ProjectId);
        Assert.Equal(area, task.Area);
        Assert.Equal(ProjectTaskType.PrepareQuotation, task.Type);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
        Assert.Equal(DateTimeKind.Utc, task.AssignedAt.Kind);
        Assert.InRange(task.AssignedAt, before, after);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);
        Assert.NotEqual(task.Id, new ProjectTask(projectId, area, ProjectTaskType.PrepareQuotation).Id);
        Assert.All(typeof(ProjectTask).GetProperties(), property => Assert.True(property.SetMethod!.IsPrivate));
    }

    [Fact]
    public void Constructor_WithEmptyProjectId_Throws()
    {
        Assert.Throws<ArgumentException>("projectId",
            () => new ProjectTask(Guid.Empty, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    public void Constructor_WithUndefinedArea_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>("area",
            () => new ProjectTask(Guid.NewGuid(), (OperationalArea)value, ProjectTaskType.PrepareQuotation));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12)]
    public void Constructor_WithUndefinedType_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>("type",
            () => new ProjectTask(Guid.NewGuid(), OperationalArea.Budgets, (ProjectTaskType)value));
    }

    [Theory]
    [InlineData(OperationalArea.Commercial, 1)]
    [InlineData(OperationalArea.Budgets, 2)]
    [InlineData(OperationalArea.Accounting, 3)]
    [InlineData(OperationalArea.Installation, 4)]
    public void AreaValues_MatchDataContract(OperationalArea area, int value) => Assert.Equal(value, (int)area);

    [Fact]
    public void TaskTypeValue_MatchesDataContract() => Assert.Equal(1, (int)ProjectTaskType.PrepareQuotation);

    [Theory]
    [InlineData(ProjectTaskStatus.Pending, 1)]
    [InlineData(ProjectTaskStatus.InProgress, 2)]
    [InlineData(ProjectTaskStatus.Completed, 3)]
    public void StatusValues_MatchDataContract(ProjectTaskStatus status, int value) => Assert.Equal(value, (int)status);
}
