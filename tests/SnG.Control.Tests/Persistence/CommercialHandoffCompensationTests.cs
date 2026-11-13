using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Configurations;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Persistence;

public class CommercialHandoffCompensationTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("file")]
    [InlineData("commit")]
    [InlineData("cancel")]
    [InlineData("cleanup")]
    [InlineData("uncertain")]
    public async Task FailedHandoff_RestoresStateAndPreservesOriginalAndImages(string phase)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var state = scenario.State!;
        using var directory = new TemporaryStorage();
        var storage = new FailingStorage(directory.Root) { FailAt = phase is "file" or "cancel" ? 1 : 0, Cancel = phase == "cancel", FailDelete = phase == "cleanup" };
        foreach (var (key, bytes) in scenario.Files) await storage.Inner.SaveAsync(key, bytes, default);
        await using var db = new SaveContext { Failure = phase == "save" ? new DbUpdateException("Synthetic") : null };
        var unrelated = new Client("Unrelated"); db.Attach(unrelated);
        await using var transaction = new TransactionDouble { FailCommit = phase is "commit" or "cleanup" or "uncertain", FailRollback = phase == "uncertain" };
        var operation = new EfBudgetQuotationCommercialHandoffStore(db, storage, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(state, new byte[] { 1, 2 }, transaction, "google-test-subject", default);
        if (phase == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else if (phase is "cleanup" or "uncertain") await Assert.ThrowsAsync<AggregateException>(() => operation);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(ProjectTaskStatus.InProgress, state.Tasks.Single().Status); Assert.Null(state.Tasks.Single().CompletedAt);
        Assert.Null(state.Version!.ConfirmedFileStorageKey);
        Assert.Equal(phase != "uncertain", transaction.RolledBack);
        foreach (var (key, bytes) in scenario.Files) Assert.Equal(bytes, await storage.Inner.ReadAsync(key, default));
        Assert.Equal(scenario.Files.Count + (phase is "cleanup" or "uncertain" ? 1 : 0), Directory.GetFiles(directory.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Same(unrelated, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.All(storage.DeleteTokens, token => Assert.False(token.IsCancellationRequested));
    }
    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, ProjectTaskConfiguration.ReviewQuotationIndexName, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, "AnotherUniqueIndex", false)]
    [InlineData(PostgresErrorCodes.CheckViolation, ProjectTaskConfiguration.ReviewQuotationIndexName, false)]
    public async Task OnlyExactReviewIndexConflict_IsTranslated(string code, string constraint, bool conflict)
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); using var directory = new TemporaryStorage(); var storage = new FailingStorage(directory.Root);
        await using var db = new SaveContext { Failure = new DbUpdateException("Synthetic", new PostgresException("Synthetic", "ERROR", "ERROR", code, constraintName: constraint)) };
        await using var transaction = new TransactionDouble();
        var operation = new EfBudgetQuotationCommercialHandoffStore(db, storage, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(scenario.State!, new byte[] { 1 }, transaction, "google-test-subject", default);
        if (conflict) Assert.Equal(CommercialHandoffStatus.AlreadySent, (await operation).Status);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(0, storage.SaveCalls); Assert.True(transaction.RolledBack); Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task Success_CommitsAfterFileAndOneSave_WithoutChangingProjectOrCatalog()
    {
        var scenario = await CommercialHandoffScenario.CreateAsync(); var state = scenario.State!;
        using var directory = new TemporaryStorage(); var storage = new FailingStorage(directory.Root);
        await using var db = new SaveContext();
        var before = System.Text.Json.JsonSerializer.Serialize(new { state.Project, state.Catalog, state.Items, state.Extractions });
        await using var transaction = new TransactionDouble
        {
            BeforeCommit = () =>
        {
            Assert.Equal(1, db.SaveCalls); Assert.True(File.Exists(Path.Combine(directory.Root, state.Version!.ConfirmedFileStorageKey!)));
        }
        };
        var result = await new EfBudgetQuotationCommercialHandoffStore(db, storage, TestTiming.Value, new NotificationWriterDouble(db)).PersistAsync(state, new byte[] { 1, 2 }, transaction, "google-test-subject", default);
        Assert.Equal(CommercialHandoffStatus.Sent, result.Status); Assert.True(transaction.Committed);
        var storedReview = Assert.Single(db.ChangeTracker.Entries<ProjectTask>(), entry => entry.Entity.Type == ProjectTaskType.ReviewQuotation).Entity;
        Assert.Equal("google-test-subject", storedReview.AssignedToGoogleSubject);
        Assert.Equal(storedReview.AssignedToGoogleSubject, result.Response!.CommercialTask.AssignedToGoogleSubject);
        Assert.Equal("budgets-test-subject", result.Response.BudgetTask.AssignedToGoogleSubject);
        Assert.Equal(1, db.SaveCalls);
        Assert.Equal(ProjectTaskStatus.Completed, result.Response!.BudgetTask.Status); Assert.Equal(ProjectTaskStatus.InProgress, result.Response.CommercialTask.Status);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(new { state.Project, state.Catalog, state.Items, state.Extractions }));
        Assert.Equal(0, result.Response.BudgetTask.CompletedAt!.Value.Ticks % 10); Assert.Equal(0, result.Response.CommercialTask.StartedAt!.Value.Ticks % 10);
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
        public bool FailDelete { get; init; }
        public int SaveCalls { get; private set; }
        public List<CancellationToken> DeleteTokens { get; } = [];
        public Task SaveAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            SaveCalls++;
            if (SaveCalls == FailAt) throw Cancel ? new OperationCanceledException() : new IOException("Synthetic write failure");
            return Inner.SaveAsync(key, bytes, token);
        }
        public Task DeleteAsync(string key, CancellationToken token) { DeleteTokens.Add(token); if (FailDelete) throw new IOException("Synthetic cleanup failure"); return Inner.DeleteAsync(key, token); }
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
