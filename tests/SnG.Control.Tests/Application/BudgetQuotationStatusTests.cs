using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.Budgets.Status;
using SnG.Control.Domain.Enums;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Application;

public class BudgetQuotationStatusTests
{
    [Theory]
    [InlineData("ready")]
    [InlineData("missing-extraction")]
    [InlineData("extra-extraction")]
    [InlineData("empty")]
    [InlineData("catalog")]
    [InlineData("sg")]
    [InlineData("workflow")]
    [InlineData("version")]
    public async Task ReadAndCommandShareTheSameReadiness(string change)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync();
        var state = scenario.State!;
        state = change switch
        {
            "missing-extraction" => state with { Extractions = state.Extractions.Skip(1).ToArray() },
            "extra-extraction" => state with { Extractions = state.Extractions.Concat(state.Extractions.Take(1)).ToArray() },
            "empty" => state with { Items = [], Extractions = [] },
            "catalog" => state with { Catalog = null },
            "workflow" => state with { Tasks = [] },
            "version" => state with { Version = null },
            _ => state
        };
        if (change == "sg") state.Project.SetSgCode("SG777");
        var result = BudgetQuotationStatusResponse.From(state.Project, state.Tasks, state.Readiness(),
            state.Version?.ConfirmedAt, state.Items.Sum(i => (long)i.Quantity));
        Assert.Equal(state.Validate() == CommercialHandoffStatus.Sent, result.CanSendToCommercial);
        if (change == "sg") Assert.Equal(BudgetQuotationSendBlockReason.SgChangedAfterConfirmation, result.SendBlockReason);
        if (change is not ("ready" or "sg" or "version")) Assert.Equal(BudgetQuotationSendBlockReason.InvalidState, result.SendBlockReason);
    }
    [Fact]
    public async Task HistoricalTasksRetainSentStatusAfterOperationalFileRemoval()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync();
        await scenario.SendAsync(scenario.State!, new byte[] { 1 }, "synthetic-commercial", default);
        var state = scenario.State!;
        state.Version!.ClearConfirmedFileStorageKey();
        state.Tasks.Single(t => t.Type == ProjectTaskType.ReviewQuotation).Complete();
        var result = BudgetQuotationStatusResponse.From(state.Project, state.Tasks, state.Readiness(), state.Version.ConfirmedAt, 4);
        Assert.Equal(BudgetQuotationStatus.SentToCommercial, result.Status);
        Assert.False(result.CanSendToCommercial);
    }
}