using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Configurations;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Persistence;

public class QuotationConfirmationCompensationTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("xlsx")]
    [InlineData("middle-image")]
    [InlineData("commit")]
    [InlineData("cancel")]
    public async Task Failure_RollsBackCleansOnlyCreatedFilesAndDetachesOnlyItsEntities(string phase)
    {
        var scenario = new ConfirmationScenario(); await scenario.ExecuteAsync(await scenario.RequestAsync());
        var data = scenario.Store.Confirmations[scenario.Project.Id];
        using var directory = new TemporaryStorage();
        var storage = new FailingStorage(directory.Root) { FailAt = phase == "xlsx" ? 1 : phase is "middle-image" or "cancel" ? 3 : 0, Cancel = phase == "cancel" };
        await storage.Inner.SaveAsync("unrelated.bin", new byte[] { 9 }, default);
        await using var db = new SaveContext { Failure = phase == "save" ? new DbUpdateException("Synthetic failure") : null };
        var unrelated = new Client("Unrelated"); db.Attach(unrelated);
        await using var transaction = new TransactionDouble { FailCommit = phase == "commit" };
        var store = new EfBudgetQuotationConfirmationStore(db, storage);
        if (phase == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PersistAsync(data, scenario.Task, transaction, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => store.PersistAsync(data, scenario.Task, transaction, default));
        Assert.True(transaction.RolledBack); Assert.False(transaction.Committed);
        Assert.Equal(new[] { Path.Combine(directory.Root, "unrelated.bin") }, Directory.GetFiles(directory.Root, "*", SearchOption.AllDirectories));
        Assert.Equal(EntityState.Unchanged, db.Entry(unrelated).State);
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Same(unrelated, entry.Entity));
        Assert.All(storage.DeleteTokens, token => Assert.False(token.IsCancellationRequested));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, BudgetQuotationVersionConfiguration.VersionIndexName, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, "AnotherUniqueIndex", false)]
    [InlineData(PostgresErrorCodes.CheckViolation, BudgetQuotationVersionConfiguration.VersionIndexName, false)]
    public async Task OnlyExactVersionUniqueViolation_IsAlreadyConfirmed(string sqlState, string constraint, bool conflict)
    {
        var scenario = new ConfirmationScenario(); await scenario.ExecuteAsync(await scenario.RequestAsync());
        var data = scenario.Store.Confirmations[scenario.Project.Id];
        using var directory = new TemporaryStorage(); var storage = new FailingStorage(directory.Root);
        await using var db = new SaveContext { Failure = new DbUpdateException("Synthetic", new PostgresException("Synthetic", "ERROR", "ERROR", sqlState, constraintName: constraint)) };
        await using var transaction = new TransactionDouble();
        var operation = new EfBudgetQuotationConfirmationStore(db, storage).PersistAsync(data, scenario.Task, transaction, default);
        if (conflict) Assert.Equal(QuotationConfirmationStatus.AlreadyConfirmed, (await operation).Status);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(0, storage.SaveCalls); Assert.True(transaction.RolledBack); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SuccessfulCommit_SeesAllFilesAlreadyPresentAndExact()
    {
        var scenario = new ConfirmationScenario(); await scenario.ExecuteAsync(await scenario.RequestAsync()); var data = scenario.Store.Confirmations[scenario.Project.Id];
        using var directory = new TemporaryStorage(); var storage = new FailingStorage(directory.Root);
        await using var db = new SaveContext();
        await using var transaction = new TransactionDouble
        {
            BeforeCommit = () =>
        {
            Assert.Equal(1, db.SaveCalls);
            foreach (var file in data.Files) Assert.Equal(file.Bytes.ToArray(), File.ReadAllBytes(Path.Combine(directory.Root, file.StorageKey)));
        }
        };
        var result = await new EfBudgetQuotationConfirmationStore(db, storage).PersistAsync(data, scenario.Task, transaction, default);
        Assert.Equal(QuotationConfirmationStatus.Created, result.Status); Assert.True(transaction.Committed); Assert.False(transaction.RolledBack);
    }

    [Fact]
    public async Task UncertainCommit_PreservesFilesWhenRollbackCannotEstablishFailure()
    {
        var scenario = new ConfirmationScenario(); await scenario.ExecuteAsync(await scenario.RequestAsync()); var data = scenario.Store.Confirmations[scenario.Project.Id];
        using var directory = new TemporaryStorage(); var storage = new FailingStorage(directory.Root);
        await using var db = new SaveContext(); await using var transaction = new TransactionDouble { FailCommit = true, FailRollback = true };
        await Assert.ThrowsAsync<AggregateException>(() => new EfBudgetQuotationConfirmationStore(db, storage).PersistAsync(data, scenario.Task, transaction, default));
        Assert.Equal(data.Files.Count, Directory.GetFiles(directory.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(storage.DeleteTokens);
    }

    private sealed class SaveContext() : SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options)
    {
        public Exception? Failure { get; init; }
        public int SaveCalls { get; private set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++; if (Failure is not null) throw Failure;
            var count = ChangeTracker.Entries().Count(entry => entry.State == EntityState.Added);
            ChangeTracker.AcceptAllChanges(); return Task.FromResult(count);
        }
    }
    private sealed class FailingStorage(string root) : IFileStorage
    {
        public Task<byte[]> ReadAsync(string key, CancellationToken token) => Inner.ReadAsync(key, token);
        public LocalFileStorage Inner { get; } = new(Options.Create(new FileStorageOptions { RootPath = root }));
        public int FailAt { get; init; }
        public bool Cancel { get; init; }
        public int SaveCalls { get; private set; }
        public List<CancellationToken> DeleteTokens { get; } = [];
        public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            SaveCalls++;
            if (SaveCalls == FailAt) throw Cancel ? new OperationCanceledException() : new IOException("Synthetic write failure");
            return Inner.SaveAsync(key, bytes, token);
        }
        public Task DeleteAsync(string key, CancellationToken token) { DeleteTokens.Add(token); return Inner.DeleteAsync(key, token); }
    }
    private sealed class TransactionDouble : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool FailCommit { get; init; }
        public bool FailRollback { get; init; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public Action? BeforeCommit { get; init; }
        public void Commit() { BeforeCommit?.Invoke(); if (FailCommit) throw new IOException("Synthetic commit failure"); Committed = true; }
        public Task CommitAsync(CancellationToken cancellationToken = default) { Commit(); return Task.CompletedTask; }
        public void Rollback() { if (FailRollback) throw new IOException("Synthetic rollback failure"); RolledBack = true; }
        public Task RollbackAsync(CancellationToken cancellationToken = default) { Rollback(); return Task.CompletedTask; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
