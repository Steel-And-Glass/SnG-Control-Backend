using System.Text.Json;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.Projects;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Tests.Application;

public class PreviewBudgetQuotationUseCaseTests
{
    [Fact]
    public async Task ValidPreview_UsesReadOperationsPropagatesTokenAndLeavesEntitiesUnchanged()
    {
        var store = new ReadStore();
        var parser = new ParserDouble();
        var before = JsonSerializer.Serialize(new { store.Project, store.Task });
        using var tokenSource = new CancellationTokenSource();
        using var stream = new MemoryStream();
        var result = Assert.IsType<PreviewBudgetQuotationResult.Success>(await new PreviewBudgetQuotationUseCase(store, store, parser, new SnG.Control.Tests.TestDoubles.IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(store.Project!.Id, stream, "test.xlsx", tokenSource.Token));
        Assert.Equal(store.Project.Id, result.Response.ProjectId); Assert.Equal("SG0012", result.Response.ProjectSgCode);
        Assert.Equal(1, result.Response.ItemCount); Assert.Equal(2, result.Response.TotalQuantity);
        Assert.Equal(before, JsonSerializer.Serialize(new { store.Project, store.Task }));
        Assert.Equal(2, store.Tokens.Count); Assert.All(store.Tokens, token => Assert.Equal(tokenSource.Token, token));
        Assert.Equal(tokenSource.Token, parser.Token); Assert.Equal(1, parser.Calls);
    }

    [Fact]
    public async Task MissingProject_DoesNotReadTasksOrParse()
    {
        var store = new ReadStore { Project = null };
        var parser = new ParserDouble();
        Assert.IsType<PreviewBudgetQuotationResult.ProjectNotFound>(await Execute(store, parser));
        Assert.Single(store.Tokens); Assert.Equal(0, parser.Calls);
    }

    [Theory]
    [InlineData("no-task")]
    [InlineData("unassigned")]
    [InlineData("other-assignee")]
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("no-sg")]
    [InlineData("wrong-area")]
    [InlineData("no-start")]
    [InlineData("has-completion")]
    public async Task InvalidWorkflow_DoesNotParseOrModifyState(string scenario)
    {
        var store = new ReadStore();
        switch (scenario)
        {
            case "unassigned": Set(store.Task!, nameof(ProjectTask.AssignedToGoogleSubject), null); break;
            case "other-assignee": Set(store.Task!, nameof(ProjectTask.AssignedToGoogleSubject), "another-assignee"); break;
            case "no-task": store.Task = null; break;
            case "pending": store.Task = new ProjectTask(store.Project!.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"); break;
            case "completed": Set(store.Task!, nameof(ProjectTask.Status), ProjectTaskStatus.Completed); break;
            case "no-sg": store.Project = new Project(Guid.NewGuid(), "Test", "City", ProductLine.Classic, "google-test-subject"); break;
            case "wrong-area": store.Task = new ProjectTask(store.Project!.Id, OperationalArea.Commercial, ProjectTaskType.PrepareQuotation, "budgets-test-subject"); store.Task.Start(); break;
            case "no-start": Set(store.Task!, nameof(ProjectTask.StartedAt), null); break;
            case "has-completion": Set(store.Task!, nameof(ProjectTask.CompletedAt), DateTime.UtcNow); break;
        }
        var parser = new ParserDouble(); var before = JsonSerializer.Serialize(new { store.Project, store.Task });
        Assert.IsType<PreviewBudgetQuotationResult.InvalidWorkflowState>(await Execute(store, parser));
        Assert.Equal(0, parser.Calls); Assert.Equal(before, JsonSerializer.Serialize(new { store.Project, store.Task }));
    }

    [Fact]
    public async Task DocumentSgMismatch_ReturnsConflict()
    {
        var store = new ReadStore(); store.Project!.SetSgCode("SG800");
        Assert.IsType<PreviewBudgetQuotationResult.SgCodeMismatch>(await Execute(store, new ParserDouble()));
        Assert.Equal("SG800", store.Project.SgCode);
    }

    [Fact]
    public async Task ParserFailure_IsReturnedAsControlledInvalidWorkbook()
    {
        var result = Assert.IsType<PreviewBudgetQuotationResult.InvalidWorkbook>(await Execute(new ReadStore(),
            new ParserDouble { Invalid = true }));
        Assert.Equal(BudgetQuotationFileError.UnsupportedWorkbookFormat, result.Error);
    }

    private static async Task<PreviewBudgetQuotationResult> Execute(ReadStore store, ParserDouble parser)
    { using var stream = new MemoryStream(); return await new PreviewBudgetQuotationUseCase(store, store, parser, new SnG.Control.Tests.TestDoubles.IdentityDouble("budgets-test-subject")).ExecuteAsync(store.Project?.Id ?? Guid.NewGuid(), stream, "test.xlsx", CancellationToken.None); }
    private static void Set(ProjectTask task, string property, object? value) => typeof(ProjectTask).GetProperty(property)!.SetValue(task, value);

    private sealed class ParserDouble : IBudgetQuotationParser
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Invalid { get; init; }
        public Task<BudgetQuotationParseResult> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
        {
            Calls++; Token = cancellationToken;
            return Task.FromResult<BudgetQuotationParseResult>(Invalid ? new BudgetQuotationParseResult.Invalid(BudgetQuotationFileError.UnsupportedWorkbookFormat)
                : new BudgetQuotationParseResult.Success(new BudgetQuotationDocument(fileName, "Client", "Project", "City", "Line", "Line", "Team", "SG0012"),
                    [new BudgetQuotationPreviewItem("01", CommercialItemSection.Main, "Zone", "System", "Glass", "Finish", "Lock", "", 1, 2.8m, 5.6m, 2, null, 15)], []));
        }
    }

    // Every mutation method fails: successful tests prove the use case only uses the existing read boundary.
    private sealed class ReadStore : IProjectStore, IProjectTaskStore
    {
        public Task<Project?> GetReadableByIdAsync(Guid id, SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> ListReadableAsync(SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) => throw new NotSupportedException();

        public Project? Project { get; set; } = new(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
        public ProjectTask? Task { get; set; }
        public List<CancellationToken> Tokens { get; } = [];
        public ReadStore() { Project!.SetSgCode("SG0012"); Task = new ProjectTask(Project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"); Task.Start(); }
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) { Tokens.Add(cancellationToken); return System.Threading.Tasks.Task.FromResult(Project); }
        public Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid id, CancellationToken cancellationToken) { Tokens.Add(cancellationToken); return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProjectTask>>(Task is null ? [] : [Task]); }
        public Task AddAsync(Project project, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected write");
        public Task<bool> TryAddPrepareQuotationAsync(ProjectTask task, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected write");
        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected read");
        public Task<IReadOnlyList<ProjectTask>> ListByAreaAsync(OperationalArea area, ProjectTaskStatus status, SnG.Control.Application.Access.TaskInboxScope scope, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected read");
    }
}
