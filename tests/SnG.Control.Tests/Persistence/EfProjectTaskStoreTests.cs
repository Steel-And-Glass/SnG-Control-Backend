using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public class EfProjectTaskStoreTests
{
    private const string ExpectedIndex = "UX_ProjectTasks_ProjectId_PrepareQuotation";

    [Fact]
    public async Task Insert_SavesTaskAndPropagatesToken_ReturnsTrue()
    {
        await using var context = new SaveContext();
        var task = NewTask();
        using var cancellation = new CancellationTokenSource();

        Assert.True(await new EfProjectTaskStore(context, TestTiming.Value, new NotificationWriterDouble(context)).TryAddPrepareQuotationAsync(task, cancellation.Token));

        Assert.Equal(1, context.SaveCalls);
        Assert.Equal(cancellation.Token, context.ReceivedToken);
        Assert.Same(task, Assert.Single(context.SavedTasks));
    }

    [Fact]
    public async Task Insert_ExpectedUniqueViolation_ReturnsFalseAndDetachesOnlyFailedTask()
    {
        await using var context = new SaveContext { Failure = DatabaseFailure(PostgresErrorCodes.UniqueViolation, ExpectedIndex) };
        var existing = NewTask();
        context.Attach(existing);
        var task = NewTask();
        var store = new EfProjectTaskStore(context, TestTiming.Value, new NotificationWriterDouble(context));

        Assert.False(await store.TryAddPrepareQuotationAsync(task, CancellationToken.None));

        Assert.Equal(EntityState.Detached, context.Entry(task).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), entry => entry.State == EntityState.Added);
        context.Failure = null;
        var next = NewTask();
        Assert.True(await store.TryAddPrepareQuotationAsync(next, CancellationToken.None));
        Assert.Same(next, Assert.Single(context.SavedTasks));
        Assert.Equal(2, context.SaveCalls);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "PK_ProjectTasks")]
    [InlineData(PostgresErrorCodes.UniqueViolation, null)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, ExpectedIndex)]
    [InlineData(PostgresErrorCodes.CheckViolation, "AnotherConstraint")]
    public async Task Insert_OtherPostgresErrors_PropagateUnchanged(string sqlState, string? constraintName)
    {
        var failure = DatabaseFailure(sqlState, constraintName);
        await using var context = new SaveContext { Failure = failure };

        var actual = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new EfProjectTaskStore(context, TestTiming.Value, new NotificationWriterDouble(context)).TryAddPrepareQuotationAsync(NewTask(), CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Insert_NonPostgresUpdateException_PropagatesUnchanged()
    {
        var failure = new DbUpdateException("Unexpected persistence failure", new InvalidOperationException());
        await using var context = new SaveContext { Failure = failure };

        var actual = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new EfProjectTaskStore(context, TestTiming.Value, new NotificationWriterDouble(context)).TryAddPrepareQuotationAsync(NewTask(), CancellationToken.None));

        Assert.Same(failure, actual);
    }

    private static ProjectTask NewTask() => new(Guid.NewGuid(), OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);

    private static DbUpdateException DatabaseFailure(string sqlState, string? constraintName) =>
        new("Test persistence failure", new PostgresException("Test error", "ERROR", "ERROR", sqlState, constraintName: constraintName));

    private sealed class SaveContext() : SnGControlDbContext(
        new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options)
    {
        public DbUpdateException? Failure { get; set; }
        public int SaveCalls { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public ProjectTask[] SavedTasks { get; private set; } = [];

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            ReceivedToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            SavedTasks = ChangeTracker.Entries<ProjectTask>()
                .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToArray();
            if (Failure is not null) throw Failure;
            ChangeTracker.AcceptAllChanges();
            return Task.FromResult(SavedTasks.Length);
        }
    }
}
