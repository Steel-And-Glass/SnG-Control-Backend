using SnG.Control.Application.Features.Budgets.AssignSg;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Application;

public class AssignProjectSgCodeUseCaseTests
{
    private readonly ProjectStoreDouble projects = new();
    private readonly ProjectTaskStoreDouble tasks = new();

    [Fact]
    public async Task Assign_Correct_AndRepeat_PreserveOriginalStartAndPropagateToken()
    {
        var project = AddProject();
        var task = AddTask(project);
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        var useCase = new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"));
        using var cancellation = new CancellationTokenSource();
        var first = Assert.IsType<AssignProjectSgCodeResult.Success>(await useCase.ExecuteAsync(project.Id, " sg0012 ", cancellation.Token)).Response;
        Assert.Equal("SG0012", first.Project.SgCode);
        Assert.Equal(ProjectTaskStatus.InProgress, first.PrepareQuotationTask.Status);
        Assert.NotNull(first.PrepareQuotationTask.StartedAt);
        Assert.Null(first.PrepareQuotationTask.CompletedAt);
        var before = DateTime.UtcNow;
        var corrected = Assert.IsType<AssignProjectSgCodeResult.Success>(await useCase.ExecuteAsync(project.Id, "sg0042", cancellation.Token)).Response;
        Assert.Equal("SG0042", corrected.Project.SgCode);
        Assert.InRange(corrected.Project.UpdatedAt, before, DateTime.UtcNow);
        Assert.Equal(first.PrepareQuotationTask, corrected.PrepareQuotationTask);
        var repeated = Assert.IsType<AssignProjectSgCodeResult.Success>(await useCase.ExecuteAsync(project.Id, " SG0042 ", cancellation.Token)).Response;
        Assert.Equal(corrected, repeated);
        Assert.Equal(project.CreatedAt, repeated.Project.CreatedAt);
        Assert.Equal(task.Id, Assert.Single(tasks.Items).Id);
        Assert.Equal(3, store.SaveCalls);
        Assert.Equal(6, store.ReceivedTokens.Count);
        Assert.All(store.ReceivedTokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task MissingProject_ReturnsNotFoundWithoutSaving()
    {
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        Assert.IsType<AssignProjectSgCodeResult.ProjectNotFound>(await new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(Guid.NewGuid(), "SG1", CancellationToken.None));
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task UnsentProject_ReturnsConflictWithoutCreatingTask()
    {
        var project = AddProject();
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        Assert.IsType<AssignProjectSgCodeResult.ProjectNotSentToBudgets>(await new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(project.Id, "SG1", CancellationToken.None));
        Assert.Equal(0, store.SaveCalls);
        Assert.Empty(tasks.Items);
        Assert.Null(project.SgCode);
    }

    [Theory]
    [InlineData(false, ProjectTaskStatus.InProgress)]
    [InlineData(true, ProjectTaskStatus.Pending)]
    [InlineData(false, ProjectTaskStatus.Completed)]
    [InlineData(true, ProjectTaskStatus.Completed)]
    public async Task InconsistentOrUnsupportedState_ReturnsConflict(bool hasSg, ProjectTaskStatus status)
    {
        var project = AddProject();
        if (hasSg) project.SetSgCode("SG1");
        var task = AddTask(project);
        if (status == ProjectTaskStatus.InProgress) task.Start();
        else if (status == ProjectTaskStatus.Completed)
            typeof(ProjectTask).GetProperty(nameof(ProjectTask.Status))!.SetValue(task, status);
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        Assert.IsType<AssignProjectSgCodeResult.InvalidWorkflowState>(await new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(project.Id, "SG2", CancellationToken.None));
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(hasSg ? "SG1" : null, project.SgCode);
        Assert.Equal(status, task.Status);
    }

    [Fact]
    public async Task StoreRejectsDuplicate_DoesNotReportSuccessOrPersistPartialState()
    {
        var project = AddProject();
        var task = AddTask(project);
        var store = new BudgetsWorkflowStoreDouble(projects, tasks) { RejectSave = true };
        Assert.IsType<AssignProjectSgCodeResult.SgCodeAlreadyInUse>(await new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(project.Id, "SG1", CancellationToken.None));
        Assert.Equal(1, store.SaveCalls);
        Assert.Null(Assert.Single(projects.Items).SgCode);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
        Assert.Null(task.StartedAt);
    }

    [Fact]
    public async Task InvalidSg_FailsBeforeSavingOrStartingTask()
    {
        var project = AddProject();
        var task = AddTask(project);
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        await Assert.ThrowsAsync<ArgumentException>(() => new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(project.Id, "SG-1", CancellationToken.None));
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task Cancellation_StopsBeforeSaving()
    {
        var store = new BudgetsWorkflowStoreDouble(projects, tasks);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AssignProjectSgCodeUseCase(store, TestTiming.Value, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(Guid.NewGuid(), "SG1", cancellation.Token));
        Assert.Equal(0, store.SaveCalls);
    }

    private Project AddProject()
    {
        var project = new Project(Guid.NewGuid(), "Project", "City", ProductLine.Classic, "google-test-subject");
        projects.Items.Add(project);
        return project;
    }

    private ProjectTask AddTask(Project project)
    {
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject");
        tasks.Items.Add(task);
        return task;
    }
}
