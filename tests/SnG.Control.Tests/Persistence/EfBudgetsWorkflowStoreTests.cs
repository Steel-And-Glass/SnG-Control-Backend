using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public class EfBudgetsWorkflowStoreTests
{
    private const string SgIndex = "IX_Projects_SgCode";

    [Fact]
    public async Task Save_SendsBothChangesInOneCallAndPropagatesToken()
    {
        await using var context = new SaveContext();
        var (project, task) = ModifiedWorkflow(context);
        await using var store = new EfBudgetsWorkflowStore(context);
        using var cancellation = new CancellationTokenSource();
        Assert.True(await store.TrySaveAsync(project, task, cancellation.Token));
        Assert.Equal(1, context.SaveCalls);
        Assert.Equal(cancellation.Token, context.ReceivedToken);
        Assert.Equal(2, context.ModifiedCount);
        Assert.Equal(0, project.UpdatedAt.Ticks % 10);
        Assert.Equal(0, task.StartedAt!.Value.Ticks % 10);
    }

    [Fact]
    public async Task Save_SgUniqueViolation_DetachesBothEntitiesAndPreservesUnrelatedTracking()
    {
        await using var context = new SaveContext { Failure = Failure(PostgresErrorCodes.UniqueViolation, SgIndex) };
        var unrelated = new Client("Unrelated");
        context.Attach(unrelated);
        var (project, task) = ModifiedWorkflow(context);
        await using var store = new EfBudgetsWorkflowStore(context);
        Assert.False(await store.TrySaveAsync(project, task, CancellationToken.None));
        Assert.Equal(EntityState.Detached, context.Entry(project).State);
        Assert.Equal(EntityState.Detached, context.Entry(task).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(unrelated).State);
        Assert.False(context.ChangeTracker.HasChanges());
        context.Failure = null;
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "UX_ProjectTasks_ProjectId_PrepareQuotation")]
    [InlineData(PostgresErrorCodes.UniqueViolation, null)]
    [InlineData(PostgresErrorCodes.CheckViolation, SgIndex)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "FK_Projects_Clients_ClientId")]
    public async Task Save_OtherDatabaseErrors_Propagate(string sqlState, string? constraint)
    {
        var failure = Failure(sqlState, constraint);
        await using var context = new SaveContext { Failure = failure };
        var (project, task) = ModifiedWorkflow(context);
        await using var store = new EfBudgetsWorkflowStore(context);
        Assert.Same(failure, await Assert.ThrowsAsync<DbUpdateException>(() => store.TrySaveAsync(project, task, CancellationToken.None)));
    }

    [Fact]
    public async Task Save_NonPostgresError_Propagates()
    {
        var failure = new DbUpdateException("Test error", new InvalidOperationException());
        await using var context = new SaveContext { Failure = failure };
        var (project, task) = ModifiedWorkflow(context);
        await using var store = new EfBudgetsWorkflowStore(context);
        Assert.Same(failure, await Assert.ThrowsAsync<DbUpdateException>(() => store.TrySaveAsync(project, task, CancellationToken.None)));
    }

    private static (Project, ProjectTask) ModifiedWorkflow(SnGControlDbContext context)
    {
        var project = new Project(Guid.NewGuid(), "Project", "City", ProductLine.Classic, "google-test-subject");
        var task = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation);
        context.AttachRange(project, task);
        project.SetSgCode("SG1");
        task.Start();
        return (project, task);
    }

    private static DbUpdateException Failure(string sqlState, string? constraint) =>
        new("Test failure", new PostgresException("Test error", "ERROR", "ERROR", sqlState, constraintName: constraint));

    private sealed class SaveContext() : SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options)
    {
        public DbUpdateException? Failure { get; set; }
        public int SaveCalls { get; private set; }
        public int ModifiedCount { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            ReceivedToken = cancellationToken;
            ModifiedCount = ChangeTracker.Entries().Count(entry => entry.State == EntityState.Modified);
            if (Failure is not null) throw Failure;
            ChangeTracker.AcceptAllChanges();
            return Task.FromResult(ModifiedCount);
        }
    }
}
