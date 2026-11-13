using System.Text.Json;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Application;

public class CommercialQuotationTests
{
    [Theory]
    [InlineData(TechnicalField.ItemNumber)]
    [InlineData(TechnicalField.Location)]
    [InlineData(TechnicalField.System)]
    [InlineData(TechnicalField.Glass)]
    [InlineData(TechnicalField.AluminumFinish)]
    [InlineData(TechnicalField.LockType)]
    [InlineData(TechnicalField.Observations)]
    [InlineData(TechnicalField.Width)]
    [InlineData(TechnicalField.Height)]
    [InlineData(TechnicalField.SquareMeters)]
    [InlineData(TechnicalField.Quantity)]
    public void EveryTechnicalField_HasIndependentTypedDifference(TechnicalField field)
    {
        var original = new CatalogItemValues("01", "Zone", "System", "Glass", "Finish", "Lock", "Note", 1, 2, 3, 4);
        string Text(TechnicalField f, string value) => f == field ? "Changed" : value;
        decimal Number(TechnicalField f, decimal value) => f == field ? 9 : value;
        var changed = new CatalogItemValues(Text(TechnicalField.ItemNumber, "01"), Text(TechnicalField.Location, "Zone"), Text(TechnicalField.System, "System"),
            Text(TechnicalField.Glass, "Glass"), Text(TechnicalField.AluminumFinish, "Finish"), Text(TechnicalField.LockType, "Lock"), Text(TechnicalField.Observations, "Note"),
            Number(TechnicalField.Width, 1), Number(TechnicalField.Height, 2), Number(TechnicalField.SquareMeters, 3), (int)Number(TechnicalField.Quantity, 4));
        Assert.Empty(CommercialTechnicalDifferences.Compare(original, original));
        var difference = Assert.Single(CommercialTechnicalDifferences.Compare(original, changed));
        Assert.Equal(field, difference.Field); Assert.NotEqual(difference.PreviousValue, difference.CurrentValue);
        Assert.Equal(field <= TechnicalField.Observations ? "Changed" : "9", difference.CurrentValue);
    }

    [Fact]
    public async Task PreviewAndConfirm_PreserveHistoryAndExactBytes_AndTransitionTasks()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!;
        var history = JsonSerializer.Serialize(new { state.Project, state.Catalog, state.Items, state.Extractions });
        var files = System.IO.Directory.GetFiles(scenario.Directory.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var temporaryKey = state.Version!.ConfirmedFileStorageKey!;
        var review = state.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation); var started = review.StartedAt;
        var before = JsonSerializer.Serialize(state);
        var preview = await scenario.Preview().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, preview.Status); Assert.True(preview.Response!.HasTechnicalDifferences);
        Assert.Equal(1, preview.Response.TechnicalDifferencesCount); Assert.Equal(123.46m, preview.Response.ProjectTotal);
        Assert.Equal(before, JsonSerializer.Serialize(state)); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
        var result = await scenario.Confirm(new IdentityDouble("google-test-subject", "changed@example.com", "Actor")).ExecuteAsync(state.Project.Id, scenario.Bytes, "folder/test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, result.Status); Assert.Equal(123.46m, result.Response!.ProjectTotal);
        var quotation = scenario.State!.Quotation!;
        Assert.Equal("google-test-subject", quotation.ConfirmedByGoogleSubject); Assert.Equal("changed@example.com", quotation.ConfirmedByEmail); Assert.Equal("Actor", quotation.ConfirmedByName);
        Assert.Equal(DateTimeKind.Utc, quotation.ConfirmedAt.Kind); Assert.Equal("test.xlsx", quotation.OriginalFileName);
        Assert.Equal(scenario.Bytes, await scenario.ReadAsync(quotation.FileStorageKey, default));
        Assert.Null(state.Version.ConfirmedFileStorageKey); Assert.False(File.Exists(Path.Combine(scenario.Directory.Root, temporaryKey)));
        foreach (var file in files.Where(f => f.Key != Path.Combine(scenario.Directory.Root, temporaryKey).Replace('/', Path.DirectorySeparatorChar)))
            Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(files.Count, System.IO.Directory.GetFiles(scenario.Directory.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(1, scenario.SaveCalls); Assert.Equal(1, scenario.DeleteCalls);
        Assert.Equal(ProjectTaskStatus.Completed, review.Status); Assert.Equal(started, review.StartedAt); Assert.NotNull(review.CompletedAt);
        var present = Assert.Single(scenario.State.Tasks, t => t.Type == ProjectTaskType.PresentQuotationToClient);
        Assert.Equal("other-google-subject", present.AssignedToGoogleSubject); Assert.Equal(ProjectTaskStatus.InProgress, present.Status); Assert.NotNull(present.StartedAt); Assert.Null(present.CompletedAt);
        Assert.Equal(3, (int)present.Type);
        Assert.Equal(history, JsonSerializer.Serialize(new { state.Project, state.Catalog, state.Items, state.Extractions }));
        Assert.Equal(state.Items.Count, scenario.Items.Count);
        var item = scenario.Items.Single(i => i.SourceRow == 15);
        Assert.Equal("Changed glass", item.Glass); Assert.Equal(7.13m, item.UnitPrice); Assert.Equal(99.99m, item.TotalPrice);
        Assert.Contains(state.Items, old => old.Id == item.CommercialCatalogItemId && old.Glass != item.Glass);
        Assert.Equal(CommercialQuotationStatus.AlreadyConfirmed, (await scenario.Confirm().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default)).Status);
        Assert.Equal(1, scenario.SaveCalls); Assert.Equal(1, scenario.DeleteCalls);
    }

    [Theory]
    [InlineData("missing", CommercialQuotationStatus.ProjectNotFound)]
    [InlineData("review", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("pending", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("assigned", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("version", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("catalog", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("items", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("extractions", CommercialQuotationStatus.InvalidWorkflowState)]
    [InlineData("key", CommercialQuotationStatus.DocumentUnavailable)]
    [InlineData("file", CommercialQuotationStatus.DocumentUnavailable)]
    [InlineData("sg", CommercialQuotationStatus.SgCodeMismatch)]
    [InlineData("upload-sg", CommercialQuotationStatus.SgCodeMismatch)]
    [InlineData("present", CommercialQuotationStatus.AlreadyConfirmed)]
    public async Task Workflow_RejectionsHaveNoWrites(string change, CommercialQuotationStatus expected)
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!; var id = state.Project.Id; var bytes = scenario.Bytes;
        switch (change)
        {
            case "missing": scenario.State = null; break;
            case "review": scenario.State = state with { Tasks = [] }; break;
            case "pending": scenario.State = state with { Tasks = [new ProjectTask(id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "google-test-subject")] }; break;
            case "assigned": typeof(ProjectTask).GetProperty(nameof(ProjectTask.AssignedToGoogleSubject))!.SetValue(state.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation), "other-google-subject"); break;
            case "version": scenario.State = state with { Version = null }; break;
            case "catalog": scenario.State = state with { Catalog = null }; break;
            case "items": scenario.State = state with { Items = [] }; break;
            case "extractions": scenario.State = state with { Extractions = [] }; break;
            case "key": state.Version!.ClearConfirmedFileStorageKey(); break;
            case "file": await scenario.Storage.DeleteAsync(state.Version!.ConfirmedFileStorageKey!, default); break;
            case "sg": state.Project.SetSgCode("SG99"); break;
            case "upload-sg": bytes = CommercialQuotationScenario.Workbook().Text("O10", "SG99").Build(); break;
            case "present": scenario.State = state with { Tasks = state.Tasks.Append(new ProjectTask(id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "other-google-subject")).ToArray() }; break;
        }
        var before = JsonSerializer.Serialize(scenario.State);
        Assert.Equal(expected, (await scenario.Preview().ExecuteAsync(id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(expected, (await scenario.Confirm().ExecuteAsync(id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(before, JsonSerializer.Serialize(scenario.State)); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
    }

    [Theory]
    [InlineData("missing-main")]
    [InlineData("extra-main")]
    [InlineData("missing-additional")]
    [InlineData("extra-additional")]
    [InlineData("moved")]
    public async Task StructuralChanges_BlockPreviewAndConfirmation(string change)
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        var book = CommercialQuotationScenario.Workbook();
        if (change == "missing-main") book.Text("A15", "");
        if (change == "extra-main") book.Item(22);
        if (change == "missing-additional") book.Text("A41", "");
        if (change == "extra-additional") book.Item(51);
        if (change == "moved") book.Text("A15", "").Item(22);
        var bytes = book.Build(); var id = scenario.State!.Project.Id;
        var preview = await scenario.Preview().ExecuteAsync(id, bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.StructuralMismatch, preview.Status);
        Assert.NotEmpty(preview.MissingItems!.Concat(preview.UnexpectedItems!));
        Assert.Equal(CommercialQuotationStatus.StructuralMismatch, (await scenario.Confirm().ExecuteAsync(id, bytes, "test.xlsx", default)).Status);
        Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
    }

    [Fact]
    public async Task ChangedNumberAndMultipleTechnicalValues_AreAllowed_ConfirmationReparses()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var id = scenario.State!.Project.Id;
        await scenario.Preview().ExecuteAsync(id, scenario.Bytes, "test.xlsx", default);
        var changed = CommercialQuotationScenario.Workbook().Text("A15", "different").Text("B15", "Different zone").Number("J15", 9).Number("O30", 1).Build();
        var result = await scenario.Confirm().ExecuteAsync(id, changed, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, result.Status); Assert.Equal(3, result.Response!.TechnicalDifferencesCount); Assert.Equal(1, result.Response.ProjectTotal);
        Assert.Equal(changed, await scenario.ReadAsync(scenario.State.Quotation!.FileStorageKey, default));
    }

    [Fact]
    public async Task Matching_UsesKeysRegardlessOfCatalogOrder_UnchangedValuesHaveNoDifferences()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        var state = scenario.State!;
        scenario.State = state with { Items = state.Items.Reverse().ToArray(), Extractions = state.Extractions.Reverse().ToArray() };
        var bytes = CommercialQuotationScenario.Workbook().Build();
        var preview = await scenario.Preview().ExecuteAsync(state.Project.Id, bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, preview.Status);
        Assert.False(preview.Response!.HasTechnicalDifferences); Assert.Equal(0, preview.Response.TechnicalDifferencesCount);
        foreach (var item in preview.Response.Items)
            Assert.Equal(state.Items.Single(i => i.Section == item.Section && i.SourceRow == item.SourceRow).Id, item.CommercialCatalogItemId);
        var confirmation = await scenario.Confirm().ExecuteAsync(state.Project.Id, bytes, "test.xlsx", default);
        Assert.Equal(CommercialQuotationStatus.Success, confirmation.Status);
        Assert.False(confirmation.Response!.HasTechnicalDifferences);
        foreach (var item in scenario.Items)
            Assert.Equal(state.Items.Single(i => i.Section == item.Section && i.SourceRow == item.SourceRow).Id, item.CommercialCatalogItemId);
    }

    [Fact]
    public async Task LegacyCreator_AllowsPreviewButNotConfirmation()
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        typeof(Project).GetProperty(nameof(Project.CreatedByGoogleSubject))!.SetValue(scenario.State!.Project, null);
        Assert.Equal(CommercialQuotationStatus.Success, (await scenario.Preview().ExecuteAsync(scenario.State.Project.Id, scenario.Bytes, "test.xlsx", default)).Status);
        Assert.Equal(CommercialQuotationStatus.ProjectCreatorUnavailable, (await scenario.Confirm().ExecuteAsync(scenario.State.Project.Id, scenario.Bytes, "test.xlsx", default)).Status);
        Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls);
    }
}
