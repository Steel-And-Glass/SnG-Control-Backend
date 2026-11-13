using System.Text.Json;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.Entities;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Application;

public class ConfirmBudgetQuotationUseCaseTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("itemNumber")]
    [InlineData("location")]
    [InlineData("system")]
    [InlineData("glass")]
    [InlineData("aluminumFinish")]
    [InlineData("lockType")]
    [InlineData("observations")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("squareMeters")]
    [InlineData("quantity")]
    [InlineData("multiple")]
    public async Task EditedValues_AreOfficialAndOriginalSnapshotSurvives(string edit)
    {
        var scenario = new ConfirmationScenario();
        var request = await scenario.RequestAsync(); var original = request.Items![0];
        var edited = edit switch
        {
            "itemNumber" => original with { ItemNumber = "  A-01  " },
            "location" => original with { Location = "  Zone  A  " },
            "system" => original with { System = "  other system  " },
            "glass" => original with { Glass = " other glass " },
            "aluminumFinish" => original with { AluminumFinish = " other finish " },
            "lockType" => original with { LockType = " other lock " },
            "observations" => original with { Observations = "  note  inside  " },
            "width" => original with { Width = 2.54m },
            "height" => original with { Height = 3.4m },
            "squareMeters" => original with { SquareMeters = 10.123m },
            "quantity" => original with { Quantity = 8 },
            "multiple" => original with { Width = 2.54m, Glass = "changed", Quantity = 8 },
            _ => original
        };
        var before = JsonSerializer.Serialize(new { scenario.Project, scenario.Task });
        using var cancellation = new CancellationTokenSource();
        var result = await scenario.ExecuteAsync(new([request.Items[1], edited]), cancellation.Token);
        Assert.Equal(QuotationConfirmationStatus.Created, result.Status);
        var saved = scenario.Store.Confirmations[scenario.Project.Id];
        Assert.Equal(new[] { 15, 24 }, saved.Items.Select(item => item.SourceRow));
        Assert.Equal(new[] { CommercialItemSection.Main, CommercialItemSection.Additional }, saved.Items.Select(item => item.Section));
        var item = saved.Items[0]; var extraction = saved.Extractions[0];
        Assert.Equal(edited.ItemNumber.Trim(), item.ItemNumber); Assert.Equal(edited.Location!.Trim(), item.Location);
        Assert.Equal(edited.System!.Trim(), item.System); Assert.Equal(edited.Glass!.Trim(), item.Glass);
        Assert.Equal(edited.AluminumFinish!.Trim(), item.AluminumFinish); Assert.Equal(edited.LockType!.Trim(), item.LockType);
        Assert.Equal(edited.Observations!.Trim(), item.Observations);
        Assert.Equal(edited.Width, item.Width); Assert.Equal(edited.Height, item.Height); Assert.Equal(edited.SquareMeters, item.SquareMeters); Assert.Equal(edited.Quantity, item.Quantity);
        Assert.Equal(original.ItemNumber, extraction.ItemNumber); Assert.Equal(original.Width, extraction.Width); Assert.Equal(original.Glass, extraction.Glass);
        Assert.Equal(original.Quantity, extraction.Quantity); Assert.Equal(item.Id, extraction.CatalogItemId);
        Assert.Equal((long)edited.Quantity + request.Items[1].Quantity, result.Response!.TotalQuantity);
        Assert.Equal(scenario.Bytes, saved.Files[0].Bytes.ToArray()); Assert.Equal(QuotationWorkbook.FirstImage, saved.Files[1].Bytes.ToArray());
        Assert.Equal(before, JsonSerializer.Serialize(new { scenario.Project, scenario.Task }));
        Assert.Equal(cancellation.Token, scenario.Store.Token); Assert.Equal(1, saved.Version.VersionNumber);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("unknown-row")]
    [InlineData("duplicate")]
    [InlineData("section")]
    [InlineData("undefined-section")]
    [InlineData("empty-number")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("area")]
    [InlineData("quantity")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    public async Task InvalidConfirmation_DoesNotReachPersistence(string kind)
    {
        var scenario = new ConfirmationScenario(); var request = await scenario.RequestAsync(); var first = request.Items![0];
        request = kind switch
        {
            "missing" => new([first]),
            "extra" => new([.. request.Items, first with { SourceRow = 100 }]),
            "unknown-row" => new([first with { SourceRow = 100 }, request.Items[1]]),
            "duplicate" => new([first, first]),
            "section" => new([first with { Section = CommercialItemSection.Additional }, request.Items[1]]),
            "undefined-section" => new([first with { Section = (CommercialItemSection)99 }, request.Items[1]]),
            "empty-number" => new([first with { ItemNumber = " " }, request.Items[1]]),
            "width" => new([first with { Width = 0 }, request.Items[1]]),
            "height" => new([first with { Height = -1 }, request.Items[1]]),
            "area" => new([first with { SquareMeters = 0 }, request.Items[1]]),
            "quantity" => new([first with { Quantity = -1 }, request.Items[1]]),
            "null-items" => new(null),
            _ => new([null!, request.Items[1]])
        };
        Assert.Equal(QuotationConfirmationStatus.InvalidConfirmation, (await scenario.ExecuteAsync(request)).Status);
        Assert.Equal(0, scenario.Store.Calls); Assert.Empty(scenario.Store.Confirmations);
    }

    [Theory]
    [InlineData("missing", QuotationConfirmationStatus.ProjectNotFound)]
    [InlineData("pending", QuotationConfirmationStatus.InvalidWorkflowState)]
    [InlineData("completed", QuotationConfirmationStatus.InvalidWorkflowState)]
    [InlineData("no-sg", QuotationConfirmationStatus.InvalidWorkflowState)]
    [InlineData("no-task", QuotationConfirmationStatus.InvalidWorkflowState)]
    [InlineData("mismatch", QuotationConfirmationStatus.SgCodeMismatch)]
    [InlineData("invalid", QuotationConfirmationStatus.InvalidWorkbook)]
    public async Task InvalidWorkflowOrWorkbook_DoesNotPersist(string kind, QuotationConfirmationStatus expected)
    {
        var scenario = new ConfirmationScenario(); var request = await scenario.RequestAsync();
        switch (kind)
        {
            case "missing": scenario.Projects.Items.Clear(); break;
            case "pending": typeof(ProjectTask).GetProperty("Status")!.SetValue(scenario.Task, ProjectTaskStatus.Pending); break;
            case "completed": typeof(ProjectTask).GetProperty("Status")!.SetValue(scenario.Task, ProjectTaskStatus.Completed); break;
            case "no-sg": typeof(Project).GetProperty("SgCode")!.SetValue(scenario.Project, null); break;
            case "no-task": scenario.Tasks.Items.Clear(); break;
            case "mismatch": scenario.Project.SetSgCode("SG900"); break;
            case "invalid": scenario.Bytes = [1, 2, 3]; break;
        }
        Assert.Equal(expected, (await scenario.ExecuteAsync(request)).Status); Assert.Equal(0, scenario.Store.Calls);
    }

    [Theory]
    [InlineData(false, CatalogItemImageStatus.Missing)]
    [InlineData(true, CatalogItemImageStatus.Ambiguous)]
    public async Task ImageWarnings_CanBeConfirmedWithoutImageFiles(bool ambiguous, CatalogItemImageStatus status)
    {
        var workbook = new QuotationWorkbook().Item(image: false);
        if (ambiguous) workbook.Image(15).Image(16);
        var scenario = new ConfirmationScenario { Bytes = workbook.Build() };
        var result = await scenario.ExecuteAsync(await scenario.RequestAsync());
        Assert.Equal(QuotationConfirmationStatus.Created, result.Status);
        var saved = scenario.Store.Confirmations[scenario.Project.Id];
        Assert.Single(saved.Files); Assert.Equal(status, saved.Extractions[0].ImageStatus);
        Assert.Null(saved.Extractions[0].ImageStorageKey); Assert.Null(saved.Extractions[0].ImageContentType);
    }

    [Fact]
    public async Task SecondConfirmation_IsConflictAndDoesNotReplaceAnything()
    {
        var scenario = new ConfirmationScenario(); var request = await scenario.RequestAsync();
        await scenario.ExecuteAsync(request); var saved = scenario.Store.Confirmations[scenario.Project.Id];
        Assert.Equal(QuotationConfirmationStatus.AlreadyConfirmed, (await scenario.ExecuteAsync(request)).Status);
        Assert.Same(saved, scenario.Store.Confirmations[scenario.Project.Id]);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/x-test", ".bin")]
    public async Task ImageExtension_ComesFromContentTypeAndBytesRemainOriginal(string contentType, string extension)
    {
        var scenario = new ConfirmationScenario { Bytes = new QuotationWorkbook().Item(image: false).Image(15, contentType: contentType).Build() };
        await scenario.ExecuteAsync(await scenario.RequestAsync());
        var saved = scenario.Store.Confirmations[scenario.Project.Id];
        Assert.EndsWith(extension, saved.Extractions[0].ImageStorageKey); Assert.Equal(contentType, saved.Extractions[0].ImageContentType);
        Assert.Equal(QuotationWorkbook.FirstImage, saved.Files[1].Bytes.ToArray());
        scenario.Project.SetSgCode("SG999");
        Assert.Equal("SG0012", saved.Version.DocumentSgCode);
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutPersistence()
    {
        var scenario = new ConfirmationScenario(); var request = await scenario.RequestAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.ExecuteAsync(request, cancellation.Token));
        Assert.Empty(scenario.Store.Confirmations);
    }
}
