using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public sealed class PortfolioPostgresFactAttribute : FactAttribute
{
    public PortfolioPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNG_PORTFOLIO_TEST_DATABASE")))
            Skip = "Requires an isolated synthetic PostgreSQL database; never application configuration.";
    }
}

public sealed class QuotationPortfolioTests
{
    [Fact]
    public void AllFiveQueriesTranslateToPostgresWithServerAggregationOrderingAndLimit()
    {
        using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
        var store = new EfManagementReadStore(db);
        Assert.Contains("sum(", store.QuotationSummaryQuery().ToQueryString());
        Assert.Contains("sum(", store.QuotationItemSummaryQuery().ToQueryString());
        foreach (var query in new[] { store.QuotationItemsQuery(false), store.QuotationItemsQuery(true) })
        {
            var sql = query.ToQueryString();
            Assert.Contains("LIMIT", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("COLLATE", sql);
            Assert.Contains("INNER JOIN", sql); Assert.DoesNotContain("CommercialCatalogItemId", sql);
            Assert.Contains("NOT EXISTS", sql);
            Assert.Contains("VersionNumber", sql);
        }
        Assert.Contains("LIMIT", store.QuotationProjectsQuery().ToQueryString());
        foreach (var dimension in Enum.GetValues<MixDimension>())
            foreach (var order in Enum.GetValues<MixOrder>())
            {
                var sql = store.MixRanking(dimension, order).ToQueryString();
                foreach (var operation in new[] { "GROUP BY", "sum(", "count(", "LIMIT", "COLLATE", "ORDER BY" })
                    Assert.Contains(operation, sql);
            }
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UseCaseBuildsCoverageAndRejectsAggregateBeyondMoneyContract()
    {
        var store = new ManagementReadStoreDouble(new(), new(), new());
        var useCase = new GetManagementQuotationPortfolioUseCase(store);
        var empty = await useCase.ExecuteAsync(default);
        Assert.Equal(Money.CurrencyCode, empty.CurrencyCode); Assert.Equal(0.00m, empty.TotalQuotedValue);
        Assert.Empty(empty.HighestValueItems.Items); Assert.False(empty.HighestValueProjects.HasMore);
        store.Portfolio = store.Portfolio with { Summary = new(2, Money.Maximum + 1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(default));
    }

    [PortfolioPostgresFact]
    public async Task RealQueriesAreBoundedExactStableAndDoNotExposeInternalIds()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_PORTFOLIO_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("quotation_portfolio_test", config.Database);
        var counter = new QueryCounter();
        await using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>()
            .UseNpgsql(config.ConnectionString).AddInterceptors(counter).Options);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        var useCase = new GetManagementQuotationPortfolioUseCase(new EfManagementReadStore(db));
        counter.Reads = 0;
        var empty = await useCase.ExecuteAsync(default);
        Assert.Equal(21, counter.Reads);
        Assert.Equal(0, empty.QuotationCount); Assert.Equal(0, empty.ItemCount); Assert.Equal(0, empty.TotalQuantity);
        Assert.Equal(0.00m, empty.TotalQuotedValue); Assert.Empty(empty.HighestUnitPriceItems.Items);
        Assert.Empty(empty.HighestValueProjects.Items);
        Assert.Equal(0, empty.Mix.Systems.TotalItems); Assert.Equal(0, empty.Mix.Systems.SpecifiedItems);
        Assert.Equal(0, empty.Mix.Systems.UnspecifiedItems); Assert.Empty(empty.Mix.Systems.ByQuantity.Items);
        Assert.Empty(empty.Mix.Glass.ByItemCount.Items); Assert.Empty(empty.Mix.AluminumFinishes.ByQuotedValue.Items);
        Assert.Empty(empty.Mix.SystemGlassCombinations.ByQuantity.Items);

        var a = AddQuotation(db, "Proyecto A", 100.00m, [(4, 10m, 30m), (1, 25m, 25m)]);
        var b = AddQuotation(db, "Proyecto B", 250.00m, [(2, 20m, 200m), (1, 50m, 50m)]);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); counter.Reads = 0;
        var result = await useCase.ExecuteAsync(default);
        Assert.Equal(21, counter.Reads); Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(2, result.QuotationCount); Assert.Equal(4, result.ItemCount); Assert.Equal(8, result.TotalQuantity);
        Assert.Equal(350.00m, result.TotalQuotedValue); Assert.Equal("COP", result.CurrencyCode);
        Assert.Equal(b, result.HighestValueProjects.Items[0].ProjectId);
        Assert.Equal(200.00m, result.HighestValueItems.Items[0].TotalPrice);
        Assert.Equal("01", result.HighestValueItems.Items[0].ItemNumber);
        Assert.Equal("02", result.HighestUnitPriceItems.Items[0].ItemNumber);
        Assert.Equal(50.00m, result.HighestUnitPriceItems.Items[0].UnitPrice);
        Assert.Equal(30m, result.HighestValueItems.Items.Single(i => i.ProjectId == a && i.Quantity == 4).TotalPrice);
        Assert.Equal(4, result.HighestValueItems.Returned); Assert.False(result.HighestValueItems.HasMore);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var key in new[] { "commercialQuotationId", "commercialCatalogItemId", "storageKey", "subject", "\"id\"" })
            Assert.DoesNotContain(key, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350.00", json);

        for (var i = 0; i < 12; i++) AddQuotation(db, "Tie", 250.00m, [(1, 50m, 200m), (1, 50m, 200m)]);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); counter.Reads = 0;
        result = await useCase.ExecuteAsync(default);
        Assert.Equal(21, counter.Reads);
        Assert.Equal(28, result.HighestValueItems.Total); Assert.Equal(14, result.HighestValueProjects.Total);
        Assert.Equal(10, result.HighestValueItems.Returned); Assert.True(result.HighestValueItems.HasMore);
        Assert.Equal(10, result.HighestUnitPriceItems.Returned); Assert.True(result.HighestUnitPriceItems.HasMore);
        Assert.Equal(10, result.HighestValueProjects.Returned); Assert.True(result.HighestValueProjects.HasMore);
        Assert.Equal("Proyecto B", result.HighestValueItems.Items[0].ProjectName);
        Assert.Equal("Proyecto B", result.HighestValueProjects.Items[0].ProjectName);
        Assert.All(result.HighestValueItems.Items.Skip(1), row => Assert.Equal("01", row.ItemNumber));
        var repeated = await useCase.ExecuteAsync(default);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(repeated));
        Assert.Empty(db.ChangeTracker.Entries());
        await VerifyMixAsync(db, useCase, counter);
    }

    private static async Task VerifyMixAsync(SnGControlDbContext db, GetManagementQuotationPortfolioUseCase useCase, QueryCounter counter)
    {
        // This method runs only in the explicitly isolated, initially empty test cluster.
        await db.CommercialQuotationItems.ExecuteDeleteAsync();
        var quotation = await db.CommercialQuotations.FirstAsync();
        var catalog = await db.CommercialCatalogs.SingleAsync(c => c.Id == quotation.CommercialCatalogId);
        void Add(string system, string glass, string finish, int quantity, decimal value, int row)
        {
            var values = new CatalogItemValues($"{row}", "Room", system, glass, finish, "", "", 1m, 1m, 1m, quantity);
            var item = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, row, values);
            db.AddRange(item, new CommercialQuotationItem(quotation.Id, item.Id, item.Section, row, values, 1m, value));
        }
        Add(" A ", " G ", " F ", 1, 0.10m, 100);
        Add("A", "G", "F", 1, 0.20m, 101);
        Add("B", "H", "J", 20, 5.00m, 102);
        Add("C", "I", "K", 3, 100.00m, 103);
        Add("a", "g", "f", 1, 0.30m, 104);
        Add("", "", "", 2, 1m, 105);
        Add(" ", "G", "", 1, 1m, 106);
        Add("A", " ", "", 1, 1m, 107);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); counter.Reads = 0;
        counter.CheckSnapshot = true;
        var result = await useCase.ExecuteAsync(default);
        counter.CheckSnapshot = false;
        Assert.Equal(21, counter.Reads);
        var mix = result.Mix;
        foreach (var dimension in new[] { mix.Systems, mix.Glass, mix.AluminumFinishes })
        {
            Assert.Equal(8, dimension.TotalItems);
            Assert.Equal(8, dimension.SpecifiedItems + dimension.UnspecifiedItems);
            Assert.Equal(4, dimension.ByQuantity.Total);
            Assert.Equal(20, dimension.ByQuantity.Items[0].TotalQuantity);
            Assert.Equal(100.00m, dimension.ByQuotedValue.Items[0].TotalQuotedValue);
        }
        Assert.Equal("B", mix.Systems.ByQuantity.Items[0].Value);
        Assert.Equal("A", mix.Systems.ByItemCount.Items[0].Value);
        Assert.Equal("C", mix.Systems.ByQuotedValue.Items[0].Value);
        Assert.Equal(3, mix.Systems.ByItemCount.Items[0].ItemCount);
        Assert.Equal(1.30m, mix.Systems.ByItemCount.Items[0].TotalQuotedValue);
        Assert.Equal("H", mix.Glass.ByQuantity.Items[0].Value);
        Assert.Equal("G", mix.Glass.ByItemCount.Items[0].Value);
        Assert.Equal("I", mix.Glass.ByQuotedValue.Items[0].Value);
        Assert.Equal("J", mix.AluminumFinishes.ByQuantity.Items[0].Value);
        Assert.Equal("F", mix.AluminumFinishes.ByItemCount.Items[0].Value);
        Assert.Equal("K", mix.AluminumFinishes.ByQuotedValue.Items[0].Value);
        Assert.Equal(0.30m, mix.AluminumFinishes.ByItemCount.Items[0].TotalQuotedValue);
        Assert.Equal(6, mix.Systems.SpecifiedItems); Assert.Equal(2, mix.Systems.UnspecifiedItems);
        Assert.Equal(6, mix.Glass.SpecifiedItems); Assert.Equal(5, mix.AluminumFinishes.SpecifiedItems);
        var combinations = mix.SystemGlassCombinations;
        Assert.Equal(5, combinations.SpecifiedItems); Assert.Equal(3, combinations.UnspecifiedItems);
        Assert.Equal(("B", "H"), (combinations.ByQuantity.Items[0].System, combinations.ByQuantity.Items[0].Glass));
        Assert.Equal(("A", "G"), (combinations.ByItemCount.Items[0].System, combinations.ByItemCount.Items[0].Glass));
        Assert.Equal(("C", "I"), (combinations.ByQuotedValue.Items[0].System, combinations.ByQuotedValue.Items[0].Glass));
        Assert.Contains(mix.Systems.ByQuantity.Items, row => row.Value == "a");
        Assert.DoesNotContain(mix.Systems.ByQuantity.Items, row => string.IsNullOrWhiteSpace(row.Value));
        var json = JsonSerializer.Serialize(mix);
        Assert.DoesNotContain("Id", json); Assert.DoesNotContain("No especificado", json);
        Assert.Contains("0.30", json);

        for (var i = 0; i < 15; i++) Add($"Tie{i:00}", $"Glass{i:00}", $"Finish{i:00}", 10, 10.00m, 200 + i);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); counter.Reads = 0;
        result = await useCase.ExecuteAsync(default);
        Assert.Equal(21, counter.Reads);
        foreach (var dimension in new[] { result.Mix.Systems, result.Mix.Glass, result.Mix.AluminumFinishes })
            foreach (var ranking in new[] { dimension.ByQuantity, dimension.ByItemCount, dimension.ByQuotedValue })
            {
                Assert.Equal(19, ranking.Total); Assert.Equal(10, ranking.Returned); Assert.True(ranking.HasMore);
            }
        Assert.Equal("Tie00", result.Mix.Systems.ByQuantity.Items[1].Value);
        Assert.Equal("Tie08", result.Mix.Systems.ByQuantity.Items[9].Value);
        Assert.Equal(19, result.Mix.SystemGlassCombinations.ByQuantity.Total);
        Assert.Equal(JsonSerializer.Serialize(result.Mix), JsonSerializer.Serialize((await useCase.ExecuteAsync(default)).Mix));

        await db.CommercialQuotationItems.ExecuteDeleteAsync();
        Add("", " ", "", 2, 1.00m, 300);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        result = await useCase.ExecuteAsync(default);
        foreach (var dimension in new[] { result.Mix.Systems, result.Mix.Glass, result.Mix.AluminumFinishes })
        {
            Assert.Equal(1, dimension.TotalItems); Assert.Equal(0, dimension.SpecifiedItems);
            Assert.Equal(1, dimension.UnspecifiedItems); Assert.Empty(dimension.ByQuantity.Items);
            Assert.Equal(0, dimension.ByItemCount.Total); Assert.False(dimension.ByQuotedValue.HasMore);
        }
        Assert.Equal(1, result.Mix.SystemGlassCombinations.UnspecifiedItems);
        Add("Overflow", "G", "F", 1, Money.Maximum, 301);
        Add("Overflow", "G", "F", 1, 0.01m, 302);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(default));
    }

    private static Guid AddQuotation(SnGControlDbContext db, string name, decimal total, (int Quantity, decimal Unit, decimal Total)[] rows)
    {
        var client = new Client("Synthetic portfolio client");
        var project = new Project(client.Id, name, "City", ProductLine.Classic, "synthetic-owner");
        var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", name, "City", "Line", "Line", "Author", "SG9001");
        var catalog = new CommercialCatalog(version.Id);
        var quotation = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", total, "synthetic-owner", null, null);
        db.AddRange(client, project, version, catalog, quotation);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var values = new CatalogItemValues($"{index + 1:00}", "Room", "System", "Glass", "Finish", "Lock", "Notes", 1m, 2m, 2m, row.Quantity);
            var item = new CommercialCatalogItem(catalog.Id, CommercialItemSection.Main, 15 + index, values);
            db.AddRange(item, new CommercialQuotationItem(quotation.Id, item.Id, item.Section, item.SourceRow, values, row.Unit, row.Total));
        }
        return project.Id;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public bool CheckSnapshot { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (CheckSnapshot) Assert.Equal(System.Data.IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
            Reads++; return ValueTask.FromResult(result);
        }
    }
}
