using SnG.Control.Application.Access;
using SnG.Control.Tests.TestDoubles;
using System.Text.Json;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Application;

public class CommercialHandoffTests
{
    [Fact]
    public async Task Send_AssignsConfiguredOpaqueHeadRatherThanProjectCreator()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync();
        var head = new IdentityDouble("  Configured-HEAD-001  ", null, null);
        var result = await new SendBudgetQuotationToCommercialUseCase(scenario, scenario, CommercialHandoffScenario.Generator(), head, new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(scenario.State!.Project.Id, default);
        Assert.Equal(CommercialHandoffStatus.Sent, result.Status);
        Assert.Equal(head.Subject, result.Response!.CommercialTask.AssignedToGoogleSubject);
        Assert.Equal(head.Subject, scenario.State!.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation).AssignedToGoogleSubject);
        Assert.NotEqual(scenario.State.Project.CreatedByGoogleSubject, result.Response.CommercialTask.AssignedToGoogleSubject);
    }

    [Theory]
    [InlineData("missing", CommercialHandoffStatus.ProjectNotFound)]
    [InlineData("no-task", CommercialHandoffStatus.InvalidState)]
    [InlineData("pending", CommercialHandoffStatus.InvalidState)]
    [InlineData("completed", CommercialHandoffStatus.AlreadySent)]
    [InlineData("no-version", CommercialHandoffStatus.InvalidState)]
    [InlineData("no-catalog", CommercialHandoffStatus.InvalidState)]
    [InlineData("no-items", CommercialHandoffStatus.InvalidState)]
    [InlineData("no-extraction", CommercialHandoffStatus.InvalidState)]
    [InlineData("sg", CommercialHandoffStatus.SgChangedAfterConfirmation)]
    [InlineData("sent", CommercialHandoffStatus.AlreadySent)]
    [InlineData("file", CommercialHandoffStatus.DocumentUnavailable)]
    [InlineData("generation", CommercialHandoffStatus.DocumentMismatch)]
    public async Task InvalidState_HasNoWrites(string change, CommercialHandoffStatus expected)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var state = scenario.State!; var id = state.Project.Id;
        switch (change)
        {
            case "missing": scenario.State = null; break;
            case "no-task": scenario.State = state with { Tasks = [] }; break;
            case "pending": scenario.State = state with { Tasks = [new ProjectTask(id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation)] }; break;
            case "completed": state.Tasks.Single().Complete(); break;
            case "no-version": scenario.State = state with { Version = null }; break;
            case "no-catalog": scenario.State = state with { Catalog = null }; break;
            case "no-items": scenario.State = state with { Items = [] }; break;
            case "no-extraction": scenario.State = state with { Extractions = [] }; break;
            case "sg": state.Project.SetSgCode("SG12"); break;
            case "sent": state.Version!.SetConfirmedFileStorageKey("existing.xlsx"); break;
            case "file": scenario.Files.Clear(); break;
            case "generation": scenario.Files[state.Version!.OriginalFileStorageKey] = [1, 2, 3]; break;
        }
        var before = JsonSerializer.Serialize(scenario.State);
        var result = await new SendBudgetQuotationToCommercialUseCase(scenario, scenario, CommercialHandoffScenario.Generator(), new IdentityDouble(), new IdentityDouble("budgets-test-subject")).ExecuteAsync(id, default);
        Assert.Equal(expected, result.Status); Assert.Equal(0, scenario.SendCalls); Assert.Empty(scenario.SavedKeys);
        Assert.Equal(before, JsonSerializer.Serialize(scenario.State));
    }
    [Fact]
    public async Task SendAndRepeatedDownload_PreserveOriginalProjectCatalogAndTaskDates()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var state = scenario.State!;
        var before = JsonSerializer.Serialize(new { state.Project, state.Items, state.Extractions, state.Catalog });
        var started = state.Tasks.Single().StartedAt; var assigned = state.Tasks.Single().AssignedAt;
        var result = await new SendBudgetQuotationToCommercialUseCase(scenario, scenario, CommercialHandoffScenario.Generator(), new IdentityDouble(), new IdentityDouble("budgets-test-subject")).ExecuteAsync(state.Project.Id, default);
        Assert.Equal(CommercialHandoffStatus.Sent, result.Status);
        Assert.Equal(ProjectTaskStatus.Completed, result.Response!.BudgetTask.Status);
        Assert.Equal(ProjectTaskStatus.InProgress, result.Response.CommercialTask.Status);
        Assert.Equal("google-test-subject", result.Response.CommercialTask.AssignedToGoogleSubject);
        Assert.Equal("google-test-subject", scenario.State!.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation).AssignedToGoogleSubject);
        Assert.Equal("budgets-test-subject", result.Response.BudgetTask.AssignedToGoogleSubject);
        Assert.NotNull(result.Response.BudgetTask.CompletedAt);
        Assert.Equal(started, result.Response.BudgetTask.StartedAt); Assert.Equal(assigned, result.Response.BudgetTask.AssignedAt);
        Assert.NotNull(result.Response.CommercialTask.StartedAt); Assert.Null(result.Response.CommercialTask.CompletedAt);
        Assert.Equal(before, JsonSerializer.Serialize(new { state.Project, state.Items, state.Extractions, state.Catalog }));
        Assert.DoesNotContain(state.Version!.OriginalFileStorageKey, scenario.SavedKeys);
        Assert.Equal(scenario.Original, scenario.Files[state.Version.OriginalFileStorageKey]);
        Assert.Single(scenario.SavedKeys);
        var snapshot = JsonSerializer.Serialize(scenario.State);
        var download = new DownloadCommercialQuotationUseCase(scenario, scenario, new ProjectReadAccess(new IdentityDouble(), new ActiveRoleDouble(SystemRole.CommercialHead)));
        var first = await download.ExecuteAsync(state.Project.Id, default); var second = await download.ExecuteAsync(state.Project.Id, default);
        Assert.Equal(scenario.Files[state.Version.ConfirmedFileStorageKey!], first.Bytes); Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(state.Version.OriginalFileName, first.FileName); Assert.Equal(snapshot, JsonSerializer.Serialize(scenario.State));
        Assert.Single(scenario.SavedKeys); Assert.Equal(1, scenario.SendCalls);
        var retry = await new SendBudgetQuotationToCommercialUseCase(scenario, scenario, CommercialHandoffScenario.Generator(), new IdentityDouble("changed-head"), new IdentityDouble("budgets-test-subject"))
            .ExecuteAsync(state.Project.Id, default);
        Assert.Equal(CommercialHandoffStatus.AlreadySent, retry.Status);
        Assert.Equal(snapshot, JsonSerializer.Serialize(scenario.State));
        Assert.Equal(2, scenario.State!.Tasks.Count); Assert.Equal(1, scenario.SendCalls);
    }
    [Fact]
    public async Task Cancellation_IsPropagatedWithoutWrites()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SendBudgetQuotationToCommercialUseCase(scenario, scenario, CommercialHandoffScenario.Generator(), new IdentityDouble(), new IdentityDouble("budgets-test-subject")).ExecuteAsync(scenario.State!.Project.Id, cancel.Token));
        Assert.Empty(scenario.SavedKeys);
    }
}
