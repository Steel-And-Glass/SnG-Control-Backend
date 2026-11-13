using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Persistence;

public sealed class DecisionPostgresFactAttribute : FactAttribute
{
    public DecisionPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_DECISION_TEST_DATABASE"))) Skip = "Requires an isolated synthetic PostgreSQL cluster.";
    }
}
public sealed class ClientDecisionPostgresTests
{
    [Fact]
    public void ModelEnforcesBothUniqueKeysAndRestrictiveRelationships()
    {
        using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
        var entity = db.Model.FindEntityType(typeof(ClientQuotationDecision))!;
        Assert.Equal(3, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        foreach (var key in new[] { "CommercialQuotationId", "PresentQuotationTaskId" })
            Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(new[] { key }));
        Assert.Equal(2000, entity.FindProperty("Note")!.GetMaxLength());
        Assert.Equal("timestamp with time zone", entity.FindProperty("RecordedAt")!.GetColumnType());
        Assert.Equal("integer", entity.FindProperty("Decision")!.GetColumnType());
    }

    [DecisionPostgresFact]
    public async Task MigrationConcurrencyRollbackAndSharedReads()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_DECISION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("client_decision_test", config.Database);
        SnGControlDbContext Db(bool fail = false) => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString)
            .AddInterceptors(new FailSave(fail)).Options);
        EfClientQuotationDecisionStore Store(SnGControlDbContext db) => new(db, TestTiming.Value,
            new EfProjectNotificationWriter(db, new ProjectNotificationPlanner(TestTiming.Value)));
        await using var setup = Db(); Assert.Empty(await setup.Database.GetAppliedMigrationsAsync());
        await setup.Database.MigrateAsync();
        var actor = new DecisionActor("advisor", "advisor@example.test", "Synthetic advisor");
        async Task<Guid> Seed()
        {
            var client = new Client("Synthetic decision client");
            var project = new Project(client.Id, "Synthetic project", "City", ProductLine.Classic, "advisor");
            var v = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
            var c = new CommercialCatalog(v.Id);
            var q = new CommercialQuotation(project.Id, v.Id, c.Id, "synthetic.xlsx", 10, "head", null, null);
            var now = TestTiming.Value.UtcNow.AddDays(-1);
            var review = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "head", now);
            review.Start(now, now.AddDays(1)); review.Complete(now);
            var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now);
            present.Start(now, now.AddHours(1));
            setup.AddRange(client, project, v, c, q, review, present); await setup.SaveChangesAsync(); setup.ChangeTracker.Clear();
            return project.Id;
        }
        var id = await Seed();
        var unavailable = await Store(setup).ReadAsync(id, new("management", SystemRole.Management), default);
        Assert.False(unavailable.Response!.Available); Assert.Null(unavailable.Response.Decision);
        await using var a = Db(); await using var b = Db();
        var results = await Task.WhenAll(Store(a).RecordAsync(id, ClientQuotationDecisionType.Approved, null, actor, default),
            Store(b).RecordAsync(id, ClientQuotationDecisionType.Rejected, "Recorded comment", actor, default));
        Assert.Single(results, r => r.Status == DecisionStatus.Success); Assert.Single(results, r => r.Status == DecisionStatus.AlreadyRecorded);
        Assert.Equal(1, await setup.ClientQuotationDecisions.CountAsync(d => d.ProjectId == id));
        var completed = await setup.ProjectTasks.AsNoTracking().SingleAsync(t => t.ProjectId == id && t.Type == ProjectTaskType.PresentQuotationToClient);
        Assert.Equal(ProjectTaskStatus.Completed, completed.Status); Assert.True(completed.WasOverdue);
        var persisted = await setup.ClientQuotationDecisions.AsNoTracking().SingleAsync(d => d.ProjectId == id);
        Assert.Equal(persisted.RecordedAt, completed.CompletedAt);
        Assert.Equal(2, await setup.ProjectTasks.CountAsync(t => t.ProjectId == id));
        Assert.Equal(DecisionStatus.Success, (await Store(setup).ReadAsync(id, new("head", SystemRole.CommercialHead), default)).Status);
        Assert.Equal(DecisionStatus.Success, (await Store(setup).ReadAsync(id, new("advisor", SystemRole.CommercialAdvisor), default)).Status);
        Assert.Equal(DecisionStatus.ProjectNotFound, (await Store(setup).ReadAsync(id, new("other", SystemRole.CommercialAdvisor), default)).Status);
        Assert.Equal(DecisionStatus.ProjectNotFound, (await Store(setup).ReadAsync(id, new("other", SystemRole.Budgets), default)).Status);
        foreach (var decision in Enum.GetValues<ClientQuotationDecisionType>())
        {
            var next = await Seed();
            await using var db = Db();
            Assert.Equal(DecisionStatus.Forbidden, (await Store(db).RecordAsync(next, decision, null, actor with { Subject = "other" }, default)).Status);
            Assert.Equal(DecisionStatus.Success, (await Store(db).RecordAsync(next, decision, null, actor, default)).Status);
        }
        var rollbackId = await Seed();
        await using (var failure = Db(true))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failure).RecordAsync(rollbackId, ClientQuotationDecisionType.ChangesRequested, null, actor, default));
        Assert.False(await setup.ClientQuotationDecisions.AnyAsync(d => d.ProjectId == rollbackId));
        Assert.Equal(ProjectTaskStatus.InProgress, (await setup.ProjectTasks.AsNoTracking().SingleAsync(t => t.ProjectId == rollbackId && t.Type == ProjectTaskType.PresentQuotationToClient)).Status);
        Assert.Equal(DecisionStatus.ProjectNotFound, (await Store(setup).RecordAsync(Guid.NewGuid(), ClientQuotationDecisionType.Approved, null, actor, default)).Status);
    }
    private sealed class FailSave(bool fail) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fail) throw new InvalidOperationException("Synthetic pre-commit failure");
            return ValueTask.FromResult(result);
        }
    }
}
