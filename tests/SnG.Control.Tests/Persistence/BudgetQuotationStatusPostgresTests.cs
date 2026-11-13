using SnG.Control.Application.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnG.Control.Application.Features.Budgets.Status;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Persistence;

public sealed class QuotationPostgresFactAttribute : FactAttribute
{
    public QuotationPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SNG_RUN_POSTGRES_SMOKE") != "1")
            Skip = "Opt-in PostgreSQL smoke; synthetic rows and rollback only.";
    }
}

public class BudgetQuotationStatusPostgresTests
{
    [QuotationPostgresFact]
    public async Task StatusUsesPersistedCatalogAndHistoricalTasks_WithRollbackAndNoFiles()
    {
        var config = new ConfigurationBuilder().AddUserSecrets(typeof(SnG.Control.Api.DependencyInjection).Assembly).AddEnvironmentVariables().Build();
        var connection = config.GetConnectionString("SnGControl");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Local PostgreSQL configuration is required.");
        await using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(connection).Options);
        var scenario = await CommercialHandoffScenario.CreateAsync();
        var state = scenario.State!;
        var client = new Client("Synthetic 4D rollback");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, state.Project.ClientId);
        // Avoid colliding with real SGs; all changes stay inside this transaction.
        var sg = "SG" + DateTime.UtcNow.Ticks;
        state.Project.SetSgCode(sg);
        typeof(BudgetQuotationVersion).GetProperty(nameof(BudgetQuotationVersion.DocumentSgCode))!.SetValue(state.Version, sg);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.Add(client); db.Add(state.Project); db.AddRange(state.Tasks);
            await db.SaveChangesAsync();
            var store = new EfBudgetQuotationStatusStore(db);
            Assert.Null(await store.GetAsync(Guid.NewGuid(), new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default));
            Assert.Equal(BudgetQuotationStatus.NotConfirmed, (await store.GetAsync(state.Project.Id, new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default))!.Status);
            db.Add(state.Version!); db.Add(state.Catalog!); db.AddRange(state.Items); db.AddRange(state.Extractions);
            await db.SaveChangesAsync();
            var result = (await store.GetAsync(state.Project.Id, new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default))!;
            Assert.True(result.CanSendToCommercial);
            Assert.Equal(state.Items.Count, result.ItemCount);
            Assert.Equal(state.Items.Sum(i => (long)i.Quantity), result.TotalQuantity);
            state.Project.SetSgCode(sg + "1"); await db.SaveChangesAsync();
            Assert.Equal(BudgetQuotationSendBlockReason.SgChangedAfterConfirmation, (await store.GetAsync(state.Project.Id, new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default))!.SendBlockReason);
            state.Tasks.Single().Complete();
            var review = new ProjectTask(state.Project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "synthetic-head");
            review.Start(); db.Add(review); await db.SaveChangesAsync();
            Assert.Equal(BudgetQuotationStatus.SentToCommercial, (await store.GetAsync(state.Project.Id, new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default))!.Status);
            review.Complete(); await db.SaveChangesAsync();
            Assert.Null(state.Version!.ConfirmedFileStorageKey);
            result = (await store.GetAsync(state.Project.Id, new ProjectReadScope("budgets-test-subject", SystemRole.Budgets), default))!;
            Assert.Equal(BudgetQuotationStatus.SentToCommercial, result.Status);
            Assert.False(result.CanSendToCommercial);
            Assert.Equal(BudgetQuotationSendBlockReason.None, result.SendBlockReason);
        }
        finally { await transaction.RollbackAsync(); }
        db.ChangeTracker.Clear();
        Assert.False(await db.Projects.AnyAsync(p => p.Id == state.Project.Id));
        Assert.False(await db.Clients.AnyAsync(c => c.Id == client.Id));
    }
}