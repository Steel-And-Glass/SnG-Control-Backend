using SnG.Control.Application.Access;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Files;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Quotations;

namespace SnG.Control.Tests.Fixtures;

public sealed class CommercialHandoffScenario : IBudgetQuotationCommercialHandoffStore, IFileStorage
{
    private readonly SemaphoreSlim handoffGate = new(1, 1);
    public CommercialHandoffState? State { get; set; }
    public Dictionary<string, byte[]> Files { get; } = [];
    public List<string> SavedKeys { get; } = [];
    public int SendCalls { get; private set; }
    public int GetFileCalls { get; private set; }
    public byte[] Original { get; private set; } = [];
    public static XlsxBudgetQuotationWorkbookGenerator Generator() => new(new XlsxBudgetQuotationParser(Options.Create(new BudgetQuotationParserOptions())));
    public static async Task<CommercialHandoffScenario> CreateAsync(byte[]? bytes = null)
    {
        var source = new ConfirmationScenario();
        if (bytes is not null) source.Bytes = bytes;
        await source.ExecuteAsync(await source.RequestAsync());
        var data = source.Store.Confirmations[source.Project.Id];
        var result = new CommercialHandoffScenario
        {
            State = new(source.Project, [source.Task], data.Version, data.Catalog, data.Items, data.Extractions),
            Original = source.Bytes
        };
        foreach (var file in data.Files) result.Files.Add(file.StorageKey, file.Bytes.ToArray());
        return result;
    }
    public Task<CommercialHandoffState?> LoadAsync(Guid id, CancellationToken token) => Task.FromResult(State?.Project.Id == id ? State : null);
    public Task<CommercialFileState> GetFileAsync(Guid id, ProjectReadScope scope, CancellationToken token)
    {
        GetFileCalls++;
        return Task.FromResult(State?.Project.Id != id || !scope.CanRead(State.Project, State.Tasks) ? new CommercialFileState(false)
            : new(true, State.Version?.ConfirmedFileStorageKey, State.Version?.OriginalFileName, State.Tasks.Any(task => task.Type == ProjectTaskType.ReviewQuotation)));
    }
    public async Task<CommercialHandoffResult> SendAsync(CommercialHandoffState expected, ReadOnlyMemory<byte> bytes, string commercialHeadSubject, CancellationToken token)
    {
        // Model the production store's serialized transaction inside this in-memory fixture.
        await handoffGate.WaitAsync(token);
        try
        {
            SendCalls++;
            var state = State!;
            if (state.Validate() != CommercialHandoffStatus.Sent) return new(state.Validate());
            var budget = state.Tasks.Single(); budget.Complete();
            var review = new ProjectTask(state.Project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, commercialHeadSubject); review.Start();
            var version = state.Version!;
            var key = $"projects/{state.Project.Id}/budget-quotations/{version.Id}/confirmed.xlsx";
            await SaveAsync(key, bytes, token); version.SetConfirmedFileStorageKey(key);
            State = state with { Tasks = [budget, review] };
            return new(CommercialHandoffStatus.Sent, new(state.Project.Id, Response(budget), Response(review), version.Id, state.Catalog!.Id, true));
        }
        finally { handoffGate.Release(); }
    }
    public Task<byte[]> ReadAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Files.TryGetValue(key, out var bytes) ? bytes.ToArray() : throw new FileNotFoundException("Synthetic missing file"));
    }
    public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); SavedKeys.Add(key); Files.Add(key, bytes.ToArray()); return Task.CompletedTask;
    }
    public Task DeleteAsync(string key, CancellationToken token) { Files.Remove(key); return Task.CompletedTask; }
    private static ProjectTaskResponse Response(ProjectTask task) => ProjectTaskResponse.FromEntity(task, TestTiming.Value);
}
