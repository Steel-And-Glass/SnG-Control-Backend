using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Infrastructure.Files;
using Microsoft.Extensions.Options;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Application.Features.ProjectTasks.SendToBudgets;
using SnG.Control.Application.Features.ProjectTasks.ListByArea;
using SnG.Control.Application.Features.ProjectTasks.ListByProject;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Application;

public class ProjectTaskUseCaseTests : IDisposable
{
    private static ProjectReadAccess ReadAccess => new(new IdentityDouble(), new ActiveRoleDouble(SystemRole.CommercialAdvisor));
    private readonly TemporaryStorage storage = new();
    private DocumentDatabaseDouble database = null!;
    private readonly List<IDisposable> stores = [];
    private SendProjectToBudgetsUseCase Send(ProjectStoreDouble projects, ProjectTaskStoreDouble tasks, bool documents = true)
    {
        database = new(projects, tasks);
        if (documents) foreach (var project in projects.Items) database.AddDocument(project.Id);
        var access = new ProjectDocumentAccess(new IdentityDouble(), new ActiveRoleDouble(SystemRole.CommercialAdvisor), new IdentityDouble("budgets-test-subject"));
        var store = database.Store(new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = storage.Root })), access, TestTiming.Value);
        stores.Add(store); return new(store);
    }
    public void Dispose() { foreach (var store in stores) store.Dispose(); storage.Dispose(); }

    [Theory]
    [InlineData(SystemRole.CommercialAdvisor, "advisor", true)]
    [InlineData(SystemRole.CommercialAdvisor, "other", false)]
    [InlineData(SystemRole.Accounting, "accountant", false)]
    [InlineData(SystemRole.Management, "manager", true)]
    public async Task CorrectionHistoryIsOnlyVisibleToAdvisorAndManagement(SystemRole role, string subject, bool visible)
    {
        var projects = new ProjectStoreDouble(); var tasks = new ProjectTaskStoreDouble { Projects = projects }; projects.Tasks = tasks.Items;
        var project = new Project(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "advisor"); projects.Items.Add(project);
        var correction = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.CorrectDepositReceipts, "advisor");
        tasks.Items.Add(correction); tasks.Items.Add(new(project.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant"));
        var result = await new ListProjectTasksByProjectUseCase(projects, tasks, TestTiming.Value,
            new(new IdentityDouble(subject), new ActiveRoleDouble(role))).ExecuteAsync(project.Id, default);
        Assert.Equal(visible, result?.Any(t => t.Id == correction.Id) == true);
    }

    [Fact]
    public async Task Send_CreatesPendingBudgetsTask_AndMapsRejectedInsertToAlreadySent()
    {
        var projects = new ProjectStoreDouble();
        var tasks = new ProjectTaskStoreDouble { Projects = projects };
        projects.Tasks = tasks.Items;
        var project = new Project(Guid.NewGuid(), "Proyecto", "Ciudad", ProductLine.Classic, "google-test-subject");
        projects.Items.Add(project);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var send = Send(projects, tasks);

        var result = Assert.IsType<SendProjectToBudgetsResult.Created>(await send.ExecuteAsync(project.Id, token));
        var saved = Assert.Single(tasks.Items);
        Assert.Equal(saved.Id, result.Task.Id);
        Assert.Equal(project.Id, result.Task.ProjectId);
        Assert.Equal(OperationalArea.Budgets, result.Task.Area);
        Assert.Equal(ProjectTaskType.PrepareQuotation, result.Task.Type);
        Assert.Equal(ProjectTaskStatus.Pending, result.Task.Status);
        Assert.Equal(saved.AssignedAt, result.Task.AssignedAt);
        Assert.Null(result.Task.StartedAt);
        Assert.Null(result.Task.CompletedAt);
        Assert.IsType<SendProjectToBudgetsResult.AlreadySent>(await send.ExecuteAsync(project.Id, token));
        Assert.Equal(1, database.Saves);
        Assert.Equal(2, database.LoadTokens.Count);
        Assert.Single(tasks.Items);
        Assert.All(projects.ReceivedTokens.Concat(tasks.ReceivedTokens).Concat(database.LoadTokens), received => Assert.Equal(token, received));
    }

    [Fact]
    public async Task Send_MissingProject_DoesNotReadTasksOrWrite()
    {
        var tasks = new ProjectTaskStoreDouble();
        var result = await Send(new ProjectStoreDouble(), tasks)
            .ExecuteAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<SendProjectToBudgetsResult.ProjectNotFound>(result);
        Assert.Empty(tasks.ReceivedTokens);
        Assert.Equal(0, tasks.InsertAttempts);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Pending)]
    [InlineData(ProjectTaskStatus.InProgress)]
    [InlineData(ProjectTaskStatus.Completed)]
    public async Task Send_StoreRejectsExistingQuotationInAnyStatus_ReturnsAlreadySent(ProjectTaskStatus status)
    {
        var projects = new ProjectStoreDouble();
        var tasks = new ProjectTaskStoreDouble { Projects = projects };
        projects.Tasks = tasks.Items;
        var project = new Project(Guid.NewGuid(), "Proyecto", "Ciudad", ProductLine.Classic, "google-test-subject");
        projects.Items.Add(project);
        tasks.Items.Add(StoredTask(project.Id, OperationalArea.Commercial, status));

        var result = await Send(projects, tasks).ExecuteAsync(project.Id, CancellationToken.None);

        Assert.IsType<SendProjectToBudgetsResult.AlreadySent>(result);
        Assert.Equal(0, database.Saves);
        Assert.Single(tasks.Items);
    }

    [Fact]
    public async Task Lists_FilterByAreaStatusAndProject_AndPropagateCancellation()
    {
        var projects = new ProjectStoreDouble();
        var tasks = new ProjectTaskStoreDouble { Projects = projects };
        projects.Tasks = tasks.Items;
        var project = new Project(Guid.NewGuid(), "Proyecto", "Ciudad", ProductLine.Classic, "google-test-subject");
        projects.Items.Add(project);
        var pending = StoredTask(project.Id, OperationalArea.Budgets, ProjectTaskStatus.Pending);
        var completed = StoredTask(project.Id, OperationalArea.Budgets, ProjectTaskStatus.Completed);
        var commercial = StoredTask(Guid.NewGuid(), OperationalArea.Commercial, ProjectTaskStatus.Pending);
        tasks.Items.AddRange([pending, completed, commercial]);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var byArea = await new ListProjectTasksByAreaUseCase(tasks, TestTiming.Value, new ProjectReadAccess(new IdentityDouble(), new ActiveRoleDouble(SystemRole.Budgets))).ExecuteAsync(
            OperationalArea.Budgets, ProjectTaskStatus.Pending, token);
        Assert.Equal(pending.Id, Assert.Single(byArea!).Id);

        var history = await new ListProjectTasksByProjectUseCase(projects, tasks, TestTiming.Value, ReadAccess).ExecuteAsync(project.Id, token);
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, task => task.Id == pending.Id);
        Assert.Contains(history, task => task.Id == completed.Id && task.Status == ProjectTaskStatus.Completed);
        Assert.All(projects.ReceivedTokens.Concat(tasks.ReceivedTokens), received => Assert.Equal(token, received));
    }

    [Fact]
    public async Task Lists_ReturnEmptyForExistingProjectAndNoAreaMatches_ButNullForMissingProject()
    {
        var projects = new ProjectStoreDouble();
        var tasks = new ProjectTaskStoreDouble { Projects = projects };
        projects.Tasks = tasks.Items;
        var project = new Project(Guid.NewGuid(), "Proyecto", "Ciudad", ProductLine.Classic, "google-test-subject");
        projects.Items.Add(project);
        var history = new ListProjectTasksByProjectUseCase(projects, tasks, TestTiming.Value, ReadAccess);

        Assert.Empty((await history.ExecuteAsync(project.Id, CancellationToken.None))!);
        var reads = tasks.ReceivedTokens.Count;
        Assert.Null(await history.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(reads, tasks.ReceivedTokens.Count);
        Assert.Empty((await new ListProjectTasksByAreaUseCase(tasks, TestTiming.Value, new ProjectReadAccess(new IdentityDouble(), new ActiveRoleDouble(SystemRole.Budgets))).ExecuteAsync(
            OperationalArea.Budgets, ProjectTaskStatus.Pending, CancellationToken.None))!);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    public async Task ListByArea_InvalidEnums_FailBeforeReading(int area, int status)
    {
        var tasks = new ProjectTaskStoreDouble();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ListProjectTasksByAreaUseCase(tasks, TestTiming.Value, new ProjectReadAccess(new IdentityDouble(), new ActiveRoleDouble(SystemRole.Budgets))).ExecuteAsync(
                (OperationalArea)area, (ProjectTaskStatus)status, CancellationToken.None));
        Assert.Empty(tasks.ReceivedTokens);
    }

    [Fact]
    public async Task Send_CanceledToken_StopsBeforeTaskOperations()
    {
        var tasks = new ProjectTaskStoreDouble();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Send(new ProjectStoreDouble(), tasks)
                .ExecuteAsync(Guid.NewGuid(), cancellation.Token));
        Assert.Empty(tasks.ReceivedTokens);
    }

    private static ProjectTask StoredTask(Guid projectId, OperationalArea area, ProjectTaskStatus status)
    {
        var task = new ProjectTask(projectId, area, ProjectTaskType.PrepareQuotation, "google-test-subject");
        if (status == ProjectTaskStatus.InProgress) task.Start();
        else if (status == ProjectTaskStatus.Completed)
            typeof(ProjectTask).GetProperty(nameof(ProjectTask.Status))!.SetValue(task, status);
        return task;
    }
}
