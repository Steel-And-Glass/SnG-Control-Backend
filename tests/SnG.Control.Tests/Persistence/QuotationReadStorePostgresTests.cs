using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public sealed class QuotationReadStorePostgresTests
{
    [QuotationPostgresFact]
    public async Task ViewsReadNormalizedSnapshotsAndImagesWithoutFilesOrWrites()
    {
        var config = new ConfigurationBuilder().AddUserSecrets(typeof(SnG.Control.Api.DependencyInjection).Assembly).AddEnvironmentVariables().Build();
        var connection = config.GetConnectionString("SnGControl");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Local PostgreSQL configuration is required.");
        await using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(connection).Options);
        var client = new Client("Synthetic persistent read rollback");
        var project = new Project(client.Id, "Synthetic", "City", ProductLine.Classic, "synthetic-owner");
        var budgetTask = new ProjectTask(project.Id, OperationalArea.Budgets, ProjectTaskType.PrepareQuotation, "synthetic-budgets");
        budgetTask.Start(); budgetTask.Complete();
        var review = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.ReviewQuotation, "synthetic-head");
        review.Start(); review.Complete();
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client in file", "Project in file", "Location", "ClientLine", "ProductionLine", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id);
        CatalogItemValues Values(string glass) => new("V1", "Room", "System", glass, "Finish", "Lock", "Notes", 2, 3, 5.75m, 2);
        var item = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, 20, Values("Confirmed glass"));
        var additional = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Additional, 120, Values("Additional glass"));
        var withoutExtraction = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, 30, Values("Legacy glass"));
        var extraction = new CommercialCatalogItemExtraction(item.Id, Values("Extracted glass"), CatalogItemImageStatus.Stored, "synthetic/not-present.png", "image/png");
        var ambiguous = new CommercialCatalogItemExtraction(additional.Id, Values("Additional glass"), CatalogItemImageStatus.Ambiguous, null, null);
        var final = new CommercialQuotation(project.Id, version.Id, catalog.Id, "final.xlsx", 987.6543m, "synthetic-head", null, null);
        var finalItem = new CommercialQuotationItem(final.Id, item.Id, item.Section, item.SourceRow, Values("Commercial glass"), 1.2345m, 456.789m);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.AddRange(client, project, budgetTask, review); await db.SaveChangesAsync();
            var store = new EfQuotationReadStore(db);
            Assert.Null(await store.GetAccessAsync(Guid.NewGuid(), default));
            Assert.Null(await store.GetBudgetAsync(project.Id, default));
            Assert.Null(await store.GetCommercialAsync(project.Id, default));
            db.AddRange(version, catalog, item, additional, withoutExtraction, extraction, ambiguous, final, finalItem); await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var budget = (await store.GetBudgetAsync(project.Id, default))!;
            Assert.Equal(version.Id, budget.QuotationVersionId); Assert.Equal(1, budget.VersionNumber); Assert.Equal(version.ConfirmedAt, budget.ConfirmedAt);
            Assert.Equal("SG9001", budget.Document.SgCode); Assert.Equal("synthetic.xlsx", budget.Document.FileName);
            Assert.Equal("Client in file", budget.Document.ClientName); Assert.Equal("Project in file", budget.Document.ProjectName);
            Assert.Equal("Location", budget.Document.Location); Assert.Equal("ClientLine", budget.Document.ClientLine);
            Assert.Equal("ProductionLine", budget.Document.ProductionLine); Assert.Equal("Author", budget.Document.PreparedBy);
            Assert.Equal(new[] { 20, 30, 120 }, budget.Items.Select(i => i.SourceRow));
            var first = budget.Items[0];
            Assert.Equal(item.Id, first.Id); Assert.Equal(CommercialItemSection.Main, first.Section);
            Assert.Equal(Values("Confirmed glass"), first.Confirmed); Assert.Equal(Values("Extracted glass"), first.Extracted);
            var difference = Assert.Single(first.TechnicalDifferences);
            Assert.Equal("Extracted glass", difference.PreviousValue); Assert.Equal("Confirmed glass", difference.CurrentValue);
            Assert.True(first.HasImage); Assert.Equal(CatalogItemImageStatus.Stored, first.ImageStatus);
            Assert.Null(budget.Items[1].Extracted); Assert.Null(budget.Items[1].ImageStatus); Assert.False(budget.Items[1].HasImage);
            Assert.Equal(CommercialItemSection.Additional, budget.Items[2].Section); Assert.Equal(CatalogItemImageStatus.Ambiguous, budget.Items[2].ImageStatus); Assert.False(budget.Items[2].HasImage);
            var commercial = (await store.GetCommercialAsync(project.Id, default))!;
            Assert.Equal(final.Id, commercial.CommercialQuotationId); Assert.Equal(final.ConfirmedAt, commercial.ConfirmedAt);
            Assert.Equal(987.65m, commercial.ProjectTotal);
            var row = Assert.Single(commercial.Items);
            Assert.Equal(item.Id, row.CommercialCatalogItemId); Assert.Equal(20, row.SourceRow); Assert.Equal(item.Section, row.Section);
            Assert.Equal(Values("Commercial glass"), row.Values); Assert.Equal(1.23m, row.UnitPrice); Assert.Equal(456.79m, row.TotalPrice);
            Assert.True(row.HasImage); Assert.Equal(CatalogItemImageStatus.Stored, row.ImageStatus);
            Assert.Equal("Confirmed glass", Assert.Single(row.TechnicalDifferences).PreviousValue);
            Assert.Equal("Commercial glass", Assert.Single(row.TechnicalDifferences).CurrentValue);
            Assert.DoesNotContain("StorageKey", JsonSerializer.Serialize(budget)); Assert.DoesNotContain("StorageKey", JsonSerializer.Serialize(commercial));
            Assert.Equal("synthetic/not-present.png", (await store.GetBudgetImageAsync(project.Id, item.Id, default))!.StorageKey);
            Assert.Null(await store.GetBudgetImageAsync(project.Id, additional.Id, default));
            Assert.Null(await store.GetBudgetImageAsync(Guid.NewGuid(), item.Id, default));
            Assert.Null(await store.GetBudgetImageAsync(project.Id, Guid.NewGuid(), default));
            Assert.Equal(2, (await store.GetAccessAsync(project.Id, default))!.Tasks.Count);
            Assert.Equal(version.OriginalFileStorageKey, await store.GetBudgetOriginalKeyAsync(project.Id, version.Id, default));
            Assert.Null(await store.GetBudgetOriginalKeyAsync(Guid.NewGuid(), version.Id, default));
            Assert.Null(await store.GetBudgetOriginalKeyAsync(project.Id, Guid.NewGuid(), default));
            var nextVersion = new BudgetQuotationVersion(project.Id, 2, "next.xlsx", "Client", "Project", "City", "Line", "Production", "Author", "SG9001");
            db.Add(nextVersion); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            Assert.Equal(version.OriginalFileStorageKey, await store.GetBudgetOriginalKeyAsync(project.Id, version.Id, default));
            Assert.Equal(nextVersion.OriginalFileStorageKey, await store.GetBudgetOriginalKeyAsync(project.Id, nextVersion.Id, default));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(JsonSerializer.Serialize(budget), JsonSerializer.Serialize(await store.GetBudgetAsync(project.Id, default)));
        }
        finally { await transaction.RollbackAsync(); }
        Assert.False(await db.Projects.AnyAsync(p => p.Id == project.Id));
        Assert.False(await db.Clients.AnyAsync(c => c.Id == client.Id));
    }
}
