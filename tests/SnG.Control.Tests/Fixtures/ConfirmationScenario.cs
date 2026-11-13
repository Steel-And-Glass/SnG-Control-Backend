using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Fixtures;

public sealed class ConfirmationScenario
{
    public ProjectStoreDouble Projects { get; } = new();
    public ProjectTaskStoreDouble Tasks { get; } = new();
    public Project Project { get; } = new(Guid.NewGuid(), "Synthetic", "City", ProductLine.Classic, "google-test-subject");
    public ProjectTask Task { get; }
    public QuotationConfirmationStoreDouble Store { get; }
    public PreviewBudgetQuotationUseCase Preview { get; }
    public ConfirmBudgetQuotationUseCase UseCase { get; }
    public byte[] Bytes { get; set; } = new QuotationWorkbook().Item().Item(23, "01", additional: true).Build();
    public ConfirmationScenario()
    {
        Project.SetSgCode("SG0012"); Projects.Items.Add(Project);
        Task = new ProjectTask(Project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets-test-subject"); Task.Start(); Tasks.Items.Add(Task);
        Store = new(Projects, Tasks);
        Preview = new(Projects, Tasks, new XlsxBudgetQuotationParser(Options.Create(new BudgetQuotationParserOptions())), new IdentityDouble("budgets-test-subject"));
        UseCase = new(Preview, Store, TestTiming.Value);
    }
    public async Task<ConfirmBudgetQuotationRequest> RequestAsync()
    {
        using var stream = new MemoryStream(Bytes);
        var preview = (PreviewBudgetQuotationResult.Success)await Preview.ExecuteAsync(Project.Id, stream, "synthetic.xlsx", CancellationToken.None);
        return new(preview.Response.Items.Select(item => new ConfirmedQuotationItem(item.Section, item.SourceRow,
            item.ItemNumber, item.Location, item.System, item.Glass, item.AluminumFinish, item.LockType, item.Observations,
            item.Width, item.Height, item.SquareMeters, item.Quantity)).ToArray());
    }
    public Task<ConfirmBudgetQuotationResult> ExecuteAsync(ConfirmBudgetQuotationRequest request, CancellationToken token = default) =>
        UseCase.ExecuteAsync(Project.Id, Bytes, "synthetic.xlsx", request, token);
}
