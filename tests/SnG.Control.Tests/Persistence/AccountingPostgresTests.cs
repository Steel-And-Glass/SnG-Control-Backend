using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Accounting;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Management;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.Requotations;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.TestDoubles;
namespace SnG.Control.Tests.Persistence;

public sealed class AccountingPostgresFactAttribute : FactAttribute
{
    public AccountingPostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_ACCOUNTING_TEST_DATABASE"))) Skip = "Requires isolated synthetic PostgreSQL."; }
}
public sealed class AccountingPostgresTests
{
    [AccountingPostgresFact]
    public async Task MigrationChainApprovedRoundAtomicHandoffAndImmutableDeposit()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_ACCOUNTING_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("accounting_test", config.Database);
        SnGControlDbContext Db(bool fail = false) => new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).AddInterceptors(new FailSave(fail)).Options);
        EfProjectNotificationWriter Notifications(SnGControlDbContext db) => new(db, new ProjectNotificationPlanner(TestTiming.Value));
        EfAccountingStore Store(SnGControlDbContext db) => new(db, TestTiming.Value, Notifications(db));
        await using var db = Db(); var migrations = db.GetService<IMigrator>();
        await migrations.MigrateAsync("20261027211911_FormalizeCommercialQuotationMoney");
        await migrations.MigrateAsync("20261028152306_AddClientQuotationDecisions");
        await migrations.MigrateAsync("20261028160515_AddVersionedRequotationRounds");
        var client = new Client("Synthetic accounting client"); var project = new Project(client.Id, "Synthetic deposit", "City", ProductLine.Classic, "advisor"); project.SetSgCode("SG9001");
        db.AddRange(client, project); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var now = TestTiming.Value.UtcNow; var actor = new DecisionActor("advisor", null, null); var accountant = new DecisionActor("accountant", null, null);
        async Task<CommercialQuotation> SeedRound(int round, ClientQuotationDecisionType? decision, Project project, ProjectTask? prepare = null)
        {
            var completedAt = TestTiming.Value.UtcNow;
            var version = new BudgetQuotationVersion(project.Id, round, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
            var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
            prepare ??= new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "budgets", now.AddDays(-1), round);
            if (prepare.Status == ProjectTaskStatus.Pending) prepare.Start(now.AddDays(-1), now.AddDays(1)); prepare.Complete(completedAt);
            var review = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "head", now.AddDays(-1), round); review.Start(now.AddDays(-1), now.AddDays(1)); review.Complete(completedAt);
            var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1), round); present.Start(now.AddDays(-1), now.AddDays(1));
            db.AddRange(version, catalog, quote, review, present);
            if (db.Entry(prepare).State == EntityState.Detached) db.Add(prepare);
            if (decision is not null) { present.Complete(completedAt); db.Add(new ClientQuotationDecision(project.Id, quote.Id, present.Id, decision.Value, "Synthetic", now, "advisor", null, null)); }
            await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return quote;
        }
        var first = await SeedRound(1, ClientQuotationDecisionType.ChangesRequested, project);
        var opened = await new EfRequotationStore(db, TestTiming.Value, Notifications(db)).RequestAsync(project.Id, "Formal round two changes", actor, "budgets", default);
        Assert.Equal(RequotationStatus.Success, opened.Status); db.ChangeTracker.Clear();
        var prepare2 = await db.ProjectTasks.SingleAsync(t => t.Id == opened.Response!.PrepareQuotationTask.Id);
        var second = await SeedRound(2, ClientQuotationDecisionType.Approved, project, prepare2);
        await migrations.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        Assert.False((await Store(db).ReadAsync(project.Id, new("manager", SystemRole.Management), default))!.AccountingHandoff.Available);
        Assert.Equal(AccountingStatus.Forbidden, (await Store(db).SendAsync(project.Id, actor with { Subject = "other" }, "accountant", default)).Status);
        await using (var failed = Db(true)) await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).SendAsync(project.Id, actor, "accountant", default));
        Assert.Empty(await db.AccountingHandoffs.ToArrayAsync()); Assert.False(await db.ProjectTasks.AnyAsync(t => t.Type == ProjectTaskType.RegisterInitialDeposit));
        await using var a = Db(); await using var b = Db();
        var handoffs = await Task.WhenAll(Store(a).SendAsync(project.Id, actor, "accountant", default), Store(b).SendAsync(project.Id, actor, "accountant", default));
        Assert.Single(handoffs, h => h.Status == AccountingStatus.Success); Assert.Single(handoffs, h => h.Status == AccountingStatus.AlreadySentToAccounting);
        var h = await db.AccountingHandoffs.AsNoTracking().SingleAsync(); Assert.Equal(second.Id, h.CommercialQuotationId); Assert.NotEqual(first.Id, h.CommercialQuotationId); Assert.Equal(2, h.QuotationRound);
        Assert.Equal(ClientQuotationDecisionType.Approved, (await db.ClientQuotationDecisions.AsNoTracking().SingleAsync(d => d.Id == h.SourceClientQuotationDecisionId)).Decision);
        var task = await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == h.RegisterInitialDepositTaskId);
        Assert.Null(task.QuotationRound); Assert.Null(task.DeadlineAt); Assert.Null(task.StartedAt); Assert.Equal(ProjectTaskStatus.Pending, task.Status);
        Assert.Equal(LeadTimeStatus.NotStarted, TestTiming.Value.Status(task, now));
        var management = new EfManagementReadStore(db);
        async Task<ManagementOverviewResponse> Overview() => await new GetManagementOverviewUseCase(management, new ListManagementTasksUseCase(management, TestTiming.Value)).ExecuteAsync(default);
        Assert.Equal(1, (await Overview()).Areas.Single(x => x.Area == OperationalArea.Accounting).Pending);
        Assert.Equal(1, await db.ProjectNotifications.CountAsync(n => n.ProjectTaskId == task.Id && n.Kind == NotificationKind.TaskAssigned));
        Assert.NotNull(await Store(db).ReadAsync(project.Id, new("accountant", SystemRole.Accounting), default));
        Assert.Null(await Store(db).ReadAsync(project.Id, new("unassigned", SystemRole.Accounting), default));
        var inbox = new EfProjectTaskStore(db, TestTiming.Value, Notifications(db));
        Assert.Single(await inbox.ListByAreaAsync(OperationalArea.Accounting, ProjectTaskStatus.Pending, new("accountant", SystemRole.Accounting), default));
        Assert.Empty(await inbox.ListByAreaAsync(OperationalArea.Accounting, ProjectTaskStatus.Pending, new("unassigned", SystemRole.Accounting), default));
        var input = new DepositInput(5000000.005m, now.AddHours(-2), "  TEST-REF  ", "  Internal note  ");
        Assert.Equal(AccountingStatus.Forbidden, (await Store(db).RecordAsync(project.Id, input, actor, default)).Status); db.ChangeTracker.Clear();
        await using (var failed = Db(true))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(failed).RecordAsync(project.Id, input, accountant, default));
            Assert.Equal(ProjectTaskStatus.Pending, failed.ChangeTracker.Entries<ProjectTask>().Single(e => e.Entity.Id == task.Id).Entity.Status);
        }
        Assert.Empty(await db.InitialDeposits.ToArrayAsync()); Assert.Equal(ProjectTaskStatus.Pending, (await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status);
        await using var c = Db(); await using var d = Db();
        var deposits = await Task.WhenAll(Store(c).RecordAsync(project.Id, input, accountant, default), Store(d).RecordAsync(project.Id, input with { Amount = 999 }, accountant, default));
        Assert.Single(deposits, r => r.Status == AccountingStatus.Success); Assert.Single(deposits, r => r.Status == AccountingStatus.AlreadyRecorded);
        var saved = await db.InitialDeposits.AsNoTracking().SingleAsync(); Assert.Equal(h.Id, saved.AccountingHandoffId); Assert.Equal(second.Id, saved.CommercialQuotationId);
        Assert.Contains(saved.Amount, new[] { 5000000.01m, 999m }); Assert.Equal("TEST-REF", saved.Reference); Assert.Equal("COP", saved.CurrencyCode);
        task = await db.ProjectTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(saved.RecordedAt, task.StartedAt); Assert.Equal(saved.RecordedAt, task.CompletedAt); Assert.NotEqual(saved.ReceivedAt, task.StartedAt);
        Assert.Null(task.DeadlineAt); Assert.Null(task.WasOverdue); Assert.Equal(LeadTimeStatus.UntrackedLegacy, TestTiming.Value.Status(task, now));
        Assert.Equal(1, (await Overview()).Areas.Single(x => x.Area == OperationalArea.Accounting).Completed);
        Assert.Equal(1, (await Overview()).LeadTime.UntrackedLegacy);
        Assert.True((await Store(db).ReadAsync(project.Id, new("accountant", SystemRole.Accounting), default))!.Deposit.Available);
        foreach (var decision in new ClientQuotationDecisionType?[] { null, ClientQuotationDecisionType.ChangesRequested, ClientQuotationDecisionType.Rejected })
        {
            var other = new Project(client.Id, "Other synthetic", "City", ProductLine.Classic, "advisor"); db.Add(other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await SeedRound(1, decision, other);
            Assert.Equal(decision is null ? AccountingStatus.ClientDecisionRequired : AccountingStatus.DecisionNotApproved, (await Store(db).SendAsync(other.Id, actor, "accountant", default)).Status);
            Assert.Equal(AccountingStatus.AccountingHandoffRequired, (await Store(db).RecordAsync(other.Id, input, accountant, default)).Status); db.ChangeTracker.Clear();
        }
        await Assert.ThrowsAsync<PostgresException>(() => migrations.MigrateAsync("20261028160515_AddVersionedRequotationRounds"));
        Assert.Equal(1, await db.InitialDeposits.CountAsync());
    }
    private sealed class FailSave(bool fail) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (fail) throw new InvalidOperationException("Synthetic pre-commit failure"); return ValueTask.FromResult(result); }
    }
}
