using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Persistence;

public class CommercialQuotationCompensationTests
{
    [Theory]
    [InlineData("save-db")]
    [InlineData("save-file")]
    [InlineData("delete-before")]
    [InlineData("delete-after")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("cleanup")]
    [InlineData("restore")]
    [InlineData("cancel")]
    [InlineData("uncertain")]
    public async Task Failure_CompensatesOnlyOwnFiles_OrReportsUncertainState(string phase)
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!;
        var preview = (await scenario.Preview().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default)).Response!;
        var quotation = new CommercialQuotation(state.Project.Id, state.Version!.Id, state.Catalog!.Id, "test.xlsx", preview.ProjectTotal, "google-test-subject", null, null);
        var items = preview.Items.Select(i => new CommercialQuotationItem(quotation.Id, i.CommercialCatalogItemId, i.Section, i.SourceRow, i.Values, i.UnitPrice, i.TotalPrice)).ToArray();
        var temporaryKey = state.Version.ConfirmedFileStorageKey!;
        var temporaryBytes = await scenario.Storage.ReadAsync(temporaryKey, default);
        var originalBytes = await scenario.Storage.ReadAsync(state.Version.OriginalFileStorageKey, default);
        var images = state.Extractions.Where(e => e.ImageStorageKey is not null).ToDictionary(e => e.ImageStorageKey!, e => File.ReadAllBytes(Path.Combine(scenario.Directory.Root, e.ImageStorageKey!)));
        using var db = new CommercialQuotationScenario.SaveContext { Failure = phase == "save-db" ? new DbUpdateException("Synthetic save failure") : null };
        var unrelated = new Client("Synthetic unrelated"); db.Attach(unrelated);
        using var transaction = new CommercialQuotationScenario.TransactionDouble { FailCommit = phase is "commit" or "cleanup" or "restore" or "uncertain", FailRollback = phase is "rollback" or "uncertain" };
        var storage = new FailureStorage(scenario, phase);
        var operation = new EfCommercialQuotationStore(db, storage, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(state, quotation, items, scenario.Bytes, preview, transaction, default);
        if (phase is "cleanup" or "restore" or "rollback" or "uncertain") await Assert.ThrowsAsync<AggregateException>(() => operation);
        else if (phase == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.False(transaction.Committed); Assert.Equal(temporaryKey, state.Version.ConfirmedFileStorageKey);
        Assert.Equal(originalBytes, await scenario.Storage.ReadAsync(state.Version.OriginalFileStorageKey, default));
        foreach (var image in images) Assert.Equal(image.Value, await scenario.Storage.ReadAsync(image.Key, default));
        if (phase is not ("restore" or "uncertain")) Assert.Equal(temporaryBytes, await scenario.Storage.ReadAsync(temporaryKey, default));
        else Assert.False(File.Exists(Path.Combine(scenario.Directory.Root, temporaryKey)));
        Assert.Equal(phase is "cleanup" or "uncertain", File.Exists(Path.Combine(scenario.Directory.Root, quotation.FileStorageKey)));
        Assert.Same(unrelated, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Equal(SnG.Control.Domain.Enums.ProjectTaskStatus.InProgress, state.Tasks.Single(t => t.Type == SnG.Control.Domain.Enums.ProjectTaskType.ReviewQuotation).Status);
    }

    [Theory]
    [InlineData("23505", "UX_CommercialQuotations_BudgetQuotationVersionId", true)]
    [InlineData("23505", "UX_ProjectTasks_ProjectId_PresentQuotationToClient", true)]
    [InlineData("23505", "OtherConstraint", false)]
    [InlineData("23514", "UX_CommercialQuotations_BudgetQuotationVersionId", false)]
    public async Task OnlyKnownUniqueConstraintsBecomeConflict(string sqlState, string constraint, bool expected)
    {
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync(); var state = scenario.State!;
        var preview = (await scenario.Preview().ExecuteAsync(state.Project.Id, scenario.Bytes, "test.xlsx", default)).Response!;
        var quotation = new CommercialQuotation(state.Project.Id, state.Version!.Id, state.Catalog!.Id, "test.xlsx", 1, "google-test-subject", null, null);
        using var db = new CommercialQuotationScenario.SaveContext { Failure = new DbUpdateException("Synthetic", new PostgresException("Synthetic", "ERROR", "ERROR", sqlState, constraintName: constraint)) };
        using var transaction = new CommercialQuotationScenario.TransactionDouble();
        var task = new EfCommercialQuotationStore(db, scenario, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(state, quotation, [], scenario.Bytes, preview, transaction, default);
        if (expected) Assert.Equal(CommercialQuotationStatus.AlreadyConfirmed, (await task).Status);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.True(transaction.RolledBack); Assert.Equal(0, scenario.SaveCalls); Assert.Equal(0, scenario.DeleteCalls); Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class FailureStorage(CommercialQuotationScenario scenario, string phase) : IFileStorage
    {
        public Task<byte[]> ReadAsync(string key, CancellationToken token) => scenario.ReadAsync(key, token);
        public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            if (key.EndsWith("commercial.xlsx") && phase is "save-file" or "rollback" or "cancel")
                throw phase == "cancel" ? new OperationCanceledException() : new IOException("Synthetic save failure");
            if (key.EndsWith("confirmed.xlsx") && phase == "restore") throw new IOException("Synthetic restore failure");
            return scenario.SaveAsync(key, bytes, token);
        }
        public async Task DeleteAsync(string key, CancellationToken token)
        {
            if (key.EndsWith("confirmed.xlsx") && phase == "delete-before") throw new IOException("Synthetic delete failure");
            if (key.EndsWith("commercial.xlsx") && phase == "cleanup") throw new IOException("Synthetic cleanup failure");
            await scenario.DeleteAsync(key, token);
            if (key.EndsWith("confirmed.xlsx") && phase == "delete-after") throw new IOException("Synthetic deleted then failed");
        }
    }
}
