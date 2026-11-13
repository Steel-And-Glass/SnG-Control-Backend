using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.Requotations;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Tests.Fixtures;
using SnG.Control.Tests.TestDoubles;

namespace SnG.Control.Tests.Persistence;

public sealed class RequotationPostgresFactAttribute : FactAttribute
{
    public RequotationPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_REQUOTATION_TEST_DATABASE"))) Skip = "Requires an isolated synthetic PostgreSQL cluster.";
    }
}
public sealed class RequotationPostgresTests
{
    [RequotationPostgresFact]
    public async Task UpgradeConcurrencyAndFullVersionedWorkflowPreserveHistory()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_REQUOTATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("requotation_test", config.Database);
        var options = new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).Options;
        SnGControlDbContext Db() => new(options);
        EfProjectNotificationWriter Notifications(SnGControlDbContext db) => new(db, new ProjectNotificationPlanner(TestTiming.Value));
        EfRequotationStore Requests(SnGControlDbContext db) => new(db, TestTiming.Value, Notifications(db));
        EfClientQuotationDecisionStore Decisions(SnGControlDbContext db) => new(db, TestTiming.Value, Notifications(db));
        await using var setup = Db();
        await setup.GetService<IMigrator>().MigrateAsync("20261028152306_AddClientQuotationDecisions");
        using var scenario = new CommercialQuotationScenario(); await scenario.InitializeAsync();
        Assert.Equal(CommercialQuotationStatus.Success, (await scenario.Confirm().ExecuteAsync(scenario.State!.Project.Id, scenario.Bytes, "synthetic.xlsx", default)).Status);
        var state = scenario.State!; var id = state.Project.Id;
        var actor = new DecisionActor(state.Project.CreatedByGoogleSubject!, "advisor@example.test", "Synthetic advisor");
        var client = new Client("Synthetic requotation client"); typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, state.Project.ClientId);
        var present = state.Tasks.Single(t => t.Type == ProjectTaskType.PresentQuotationToClient);
        present.Complete(TestTiming.Value.UtcNow);
        var decision = new ClientQuotationDecision(id, state.Quotation!.Id, present.Id, ClientQuotationDecisionType.ChangesRequested,
            "Client requested changes", TestTiming.Value.UtcNow, actor.Subject, actor.Email, actor.Name);
        await using (var legacy = new LegacyContext(options))
        {
            legacy.AddRange(client, state.Project, state.Version!, state.Catalog!, state.Quotation, decision);
            legacy.AddRange(state.Tasks); legacy.AddRange(state.Items); legacy.AddRange(state.Extractions); legacy.AddRange(scenario.Items);
            await legacy.SaveChangesAsync();
        }
        var oldFile = await scenario.Storage.ReadAsync(state.Quotation.FileStorageKey, default);
        await setup.Database.MigrateAsync();
        Assert.False(setup.Database.HasPendingModelChanges());
        Assert.All(await setup.ProjectTasks.ToArrayAsync(), t => Assert.Equal(1, t.QuotationRound)); setup.ChangeTracker.Clear();
        Assert.Equal(decision.Id, (await setup.ClientQuotationDecisions.SingleAsync()).Id); setup.ChangeTracker.Clear();
        Assert.Equal(RequotationStatus.Forbidden, (await Requests(setup).RequestAsync(id, "Changes", actor with { Subject = "other" }, "budgets-test-subject", default)).Status);
        await using (var fail = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).AddInterceptors(new FailSave()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Requests(fail).RequestAsync(id, "Rollback", actor, "budgets-test-subject", default));
        Assert.Empty(await setup.RequotationRequests.ToArrayAsync()); Assert.Equal(3, await setup.ProjectTasks.CountAsync());
        await AssertPortfolio(1);
        await using var a = Db(); await using var b = Db();
        var concurrent = await Task.WhenAll(Requests(a).RequestAsync(id, "  Change glass  ", actor, "budgets-test-subject", default), Requests(b).RequestAsync(id, "Change glass", actor, "budgets-test-subject", default));
        Assert.Single(concurrent, r => r.Status == RequotationStatus.Success); Assert.Single(concurrent, r => r.Status == RequotationStatus.AlreadyRequested);
        var opened = concurrent.Single(r => r.Status == RequotationStatus.Success).Response!;
        Assert.Equal(2, opened.PrepareQuotationTask.QuotationRound); Assert.Equal(ProjectTaskStatus.InProgress, opened.PrepareQuotationTask.Status);
        Assert.NotNull(opened.PrepareQuotationTask.StartedAt); Assert.NotNull(opened.PrepareQuotationTask.DeadlineAt);
        Assert.Equal("Change glass", opened.Request.RequestedChanges);
        Assert.Equal(state.Project.SgCode, (await setup.Projects.AsNoTracking().SingleAsync()).SgCode);
        Assert.Equal(1, (await new EfQuotationReadStore(setup).GetCommercialAsync(id, default))!.VersionNumber);
        Assert.Equal(1, (await Decisions(setup).ReadAsync(id, new("manager", SystemRole.Management), default)).Response!.QuotationRound);
        await AssertPortfolio(1);
        await ConfirmRound(2);
        await AssertPortfolio(2);
        var pending = (await Decisions(setup).ReadAsync(id, new("manager", SystemRole.Management), default)).Response!;
        Assert.False(pending.Available); Assert.Equal(2, pending.QuotationRound); Assert.Null(pending.Decision);
        Assert.Equal(DecisionStatus.Success, (await Decisions(setup).RecordAsync(id, ClientQuotationDecisionType.ChangesRequested, "Second client comment", actor, default)).Status); setup.ChangeTracker.Clear();
        Assert.Equal(RequotationStatus.Success, (await Requests(setup).RequestAsync(id, "Second formal changes", actor, "budgets-test-subject", default)).Status); setup.ChangeTracker.Clear();
        await AssertPortfolio(2);
        await ConfirmRound(3);
        await AssertPortfolio(3);
        Assert.Equal(DecisionStatus.Success, (await Decisions(setup).RecordAsync(id, ClientQuotationDecisionType.Approved, null, actor, default)).Status); setup.ChangeTracker.Clear();
        Assert.Equal(RequotationStatus.DecisionDoesNotRequestChanges, (await Requests(setup).RequestAsync(id, "Not permitted", actor, "budgets-test-subject", default)).Status);
        var history = (await Requests(setup).ReadAsync(id, new("manager", SystemRole.Management), default))!;
        Assert.Equal(3, history.Total); Assert.Equal(3, history.Returned); Assert.False(history.HasMore);
        Assert.Equal(new[] { 1, 2, 3 }, history.Rounds.Select(r => r.RoundNumber));
        Assert.Null(history.Rounds[0].RequotationRequest); Assert.Equal("Change glass", history.Rounds[1].RequotationRequest!.RequestedChanges);
        Assert.Equal(ClientQuotationDecisionType.ChangesRequested, history.Rounds[0].ClientDecision!.Decision);
        Assert.Equal(ClientQuotationDecisionType.Approved, history.Rounds[2].ClientDecision!.Decision);
        Assert.Null(await Requests(setup).ReadAsync(id, new("other", SystemRole.CommercialAdvisor), default));
        Assert.NotNull(await Requests(setup).ReadAsync(id, new("budgets-test-subject", SystemRole.Budgets), default));
        Assert.Equal(oldFile, await scenario.Storage.ReadAsync(state.Quotation.FileStorageKey, default));
        Assert.Equal(3, await setup.CommercialQuotations.CountAsync()); Assert.Equal(3, await setup.BudgetQuotationVersions.CountAsync());
        Assert.Equal(9, await setup.ProjectTasks.CountAsync()); Assert.Equal(2, await setup.RequotationRequests.CountAsync());
        Assert.Equal(3, (await new EfQuotationReadStore(setup).GetBudgetAsync(id, default))!.VersionNumber);
        Assert.Equal(3, (await new EfQuotationReadStore(setup).GetCommercialAsync(id, default))!.VersionNumber);

        async Task AssertPortfolio(int round)
        {
            await using var db = Db();
            var v = await db.BudgetQuotationVersions.AsNoTracking().SingleAsync(v => v.ProjectId == id && v.VersionNumber == round);
            var q = await db.CommercialQuotations.AsNoTracking().SingleAsync(q => q.BudgetQuotationVersionId == v.Id);
            var items = await db.CommercialQuotationItems.AsNoTracking().Where(i => i.CommercialQuotationId == q.Id).ToArrayAsync();
            var result = await new EfManagementReadStore(db).GetQuotationPortfolioAsync(default);
            Assert.Equal(1, result.Summary.QuotationCount); Assert.Equal(q.ProjectTotal, result.Summary.TotalQuotedValue);
            Assert.Equal(items.Length, result.ItemSummary.ItemCount); Assert.Equal(items.Sum(i => (long)i.Quantity), result.ItemSummary.TotalQuantity);
            Assert.Equal(q.ProjectTotal, Assert.Single(result.HighestValueProjects).ProjectTotal);
            Assert.Equal(items.Select(i => i.TotalPrice).Order(), result.HighestValueItems.Select(i => i.TotalPrice).Order());
            Assert.Equal(items.Select(i => i.UnitPrice).Order(), result.HighestUnitPriceItems.Select(i => i.UnitPrice).Order());
            Assert.Equal(items.Sum(i => (long)i.Quantity), result.Mix.Glass.ByQuantity.Items.Sum(i => i.TotalQuantity));
            Assert.Equal(items.Sum(i => i.TotalPrice), result.Mix.Glass.ByQuotedValue.Items.Sum(i => i.TotalQuotedValue));
            if (round > 1)
            {
                Assert.DoesNotContain(result.Mix.Glass.ByQuantity.Items, i => i.Value == "Changed glass");
                Assert.Contains(result.Mix.SystemGlassCombinations.ByQuantity.Items, i => i.Glass == "Round " + round);
            }
            Assert.Equal(q.Id, (await new EfCommercialQuotationStore(db, scenario.Storage, TestTiming.Value, Notifications(db)).GetFileAsync(id, new("manager", SystemRole.Management), default))!.Quotation!.Id);
            Assert.NotEmpty(await scenario.Storage.ReadAsync(v.OriginalFileStorageKey, default));
        }
        async Task ConfirmRound(int round)
        {
            await using var db = Db(); var storage = scenario.Storage;
            var bytes = CommercialQuotationScenario.Workbook().Number("N15", 20 * round).Number("O15", 40 * round).Number("O30", 100 * round).Text("D15", "Round " + round).Build();
            var preview = new PreviewBudgetQuotationUseCase(new EfProjectStore(db), new EfProjectTaskStore(db, TestTiming.Value, Notifications(db)),
                new XlsxBudgetQuotationParser(Options.Create(new BudgetQuotationParserOptions())), new IdentityDouble("budgets-test-subject"));
            using var stream = new MemoryStream(bytes);
            var parsed = Assert.IsType<PreviewBudgetQuotationResult.Success>(await preview.ExecuteAsync(id, stream, "synthetic.xlsx", default)).Response;
            Assert.Equal(round, parsed.QuotationRound);
            var input = new ConfirmBudgetQuotationRequest(parsed.Items.Select(i => new ConfirmedQuotationItem(i.Section, i.SourceRow, i.ItemNumber, i.Location, i.System, i.Glass, i.AluminumFinish, i.LockType, i.Observations, i.Width, i.Height, i.SquareMeters, i.Quantity)).ToArray());
            var confirmation = await new ConfirmBudgetQuotationUseCase(preview, new EfBudgetQuotationConfirmationStore(db, storage), TestTiming.Value).ExecuteAsync(id, bytes, "synthetic.xlsx", input, default);
            Assert.Equal(QuotationConfirmationStatus.Created, confirmation.Status); db.ChangeTracker.Clear();
            var handoff = new EfBudgetQuotationCommercialHandoffStore(db, storage, TestTiming.Value, Notifications(db));
            Assert.Equal(CommercialHandoffStatus.Sent, (await new SendBudgetQuotationToCommercialUseCase(handoff, storage, CommercialHandoffScenario.Generator(), new IdentityDouble(), new IdentityDouble("budgets-test-subject")).ExecuteAsync(id, default)).Status); db.ChangeTracker.Clear();
            var commercial = new EfCommercialQuotationStore(db, storage, TestTiming.Value, Notifications(db));
            var cp = new PreviewCommercialQuotationUseCase(commercial, CommercialQuotationScenario.Parser(), storage, new IdentityDouble());
            Assert.Equal(CommercialQuotationStatus.Success, (await new ConfirmCommercialQuotationUseCase(cp, commercial, new IdentityDouble(), new IdentityDouble()).ExecuteAsync(id, bytes, "commercial.xlsx", default)).Status); db.ChangeTracker.Clear();
            Assert.Equal(RequotationStatus.ClientDecisionRequired, (await Requests(db).RequestAsync(id, "No decision", actor, "budgets-test-subject", default)).Status);
        }
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Synthetic failure");
    }
    private sealed class LegacyContext(DbContextOptions<SnGControlDbContext> options) : SnGControlDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); modelBuilder.Ignore<RequotationRequest>();
            modelBuilder.Entity<ProjectTask>().Ignore(t => t.QuotationRound);
        }
    }
}
