using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public sealed class MoneyPostgresFactAttribute : FactAttribute
{
    public MoneyPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_MONEY_TEST_DATABASE")))
            Skip = "Requires an isolated synthetic PostgreSQL cluster; never uses application configuration.";
    }
}

public sealed class MoneyMigrationPostgresTests
{
    [MoneyPostgresFact]
    public async Task HistoricalValuesRoundWithoutLosingRowsKeysOrTechnicalPrecision()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_MONEY_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("money_contract_test", config.Database);
        await using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString).Options);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016051339_AddProjectDocumentsAndHandoff");
        var client = new Client("Synthetic money migration");
        var project = new Project(client.Id, "Synthetic", "City", ProductLine.Classic, "synthetic-owner");
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id);
        var values = new CatalogItemValues("V5", "Room", "System", "Glass", "Finish", "Lock", "Notes", 1.1234m, 2.5678m, 11.074m, 3);
        var item = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, 15, values);
        var quotation = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 0, "synthetic-owner", null, null);
        var commercial = new CommercialQuotationItem(quotation.Id, item.Id, item.Section, item.SourceRow, values, 0, 0);
        db.AddRange(client, project, version, catalog, item, quotation, commercial); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "CommercialQuotations" SET "ProjectTotal" = 98765432.167777;
            UPDATE "CommercialQuotationItems" SET "UnitPrice" = 5513429.138030602, "TotalPrice" = 48609963.17607693;
            """);
        const string constraints = """
            SELECT conname || ':' || pg_get_constraintdef(oid) AS "Value"
            FROM pg_constraint WHERE conrelid IN ('"CommercialQuotations"'::regclass, '"CommercialQuotationItems"'::regclass)
            ORDER BY conname
            """;
        var before = await db.Database.SqlQueryRaw<string>(constraints).ToArrayAsync();
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        Assert.Equal(before, await db.Database.SqlQueryRaw<string>(constraints).ToArrayAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(quotation.Id, (await db.CommercialQuotations.SingleAsync()).Id);
        Assert.Equal(commercial.Id, (await db.CommercialQuotationItems.SingleAsync()).Id);
        Assert.Equal(item.Id, (await db.CommercialCatalogItems.SingleAsync()).Id);
        Assert.Equal(version.Id, (await db.BudgetQuotationVersions.SingleAsync()).Id);
        var view = (await new EfQuotationReadStore(db).GetCommercialAsync(project.Id, default))!;
        Assert.Equal("COP", view.CurrencyCode); Assert.Equal(98765432.17m, view.ProjectTotal);
        var row = Assert.Single(view.Items);
        Assert.Equal(5513429.14m, row.UnitPrice); Assert.Equal(48609963.18m, row.TotalPrice);
        Assert.Equal(11.074m, row.Values.SquareMeters); Assert.Equal(1.1234m, row.Values.Width);
        var scales = await db.Database.SqlQueryRaw<int>("""
            SELECT numeric_scale AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND
            ((table_name = 'CommercialQuotations' AND column_name = 'ProjectTotal') OR
             (table_name = 'CommercialQuotationItems' AND column_name IN ('UnitPrice', 'TotalPrice')))
            """).ToArrayAsync();
        Assert.Equal(3, scales.Length); Assert.All(scales, scale => Assert.Equal(2, scale));
        var rounds = await db.Database.SqlQueryRaw<decimal>("""
            SELECT ROUND(value, 2) AS "Value" FROM (VALUES (1.225::numeric), (1.235::numeric), (-1.235::numeric)) AS sample(value)
            ORDER BY value
            """).ToArrayAsync();
        Assert.Equal(new[] { -1.24m, 1.23m, 1.24m }, rounds);
    }
}
