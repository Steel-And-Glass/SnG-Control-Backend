using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
namespace SnG.Control.Tests.Persistence;
public sealed class MeasurementMigrationPostgresTests
{
    [VerificationPostgresFact]
    public async Task UpgradePreservesLegacyMeasuredRowsAndFrozenSnapshotWithoutInventingTechnicalData()
    {
        var config = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal("verification_test", config.Database);
        var schema = "measurement_upgrade_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(config.ConnectionString); await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await command.ExecuteNonQueryAsync();
        config.SearchPath = schema;
        await using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        await db.Database.MigrateAsync();
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var actor = new SnG.Control.Application.Features.ClientQuotationDecisions.DecisionActor("installer", null, null);
            var client = new Client("Measurement synthetic"); var project = new Project(client.Id, "Measurement synthetic", "City", ProductLine.Classic, "advisor");
            db.AddRange(client, project); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var version = new BudgetQuotationVersion(project.Id, 1, "synthetic.xlsx", "Client", "Project", "City", "Line", "Line", "Author", "SG9001");
            var catalog = new CommercialCatalog(version.Id); var quote = new CommercialQuotation(project.Id, version.Id, catalog.Id, "synthetic.xlsx", 100, "head", null, null);
            var present = new ProjectTask(project.Id, OperationalArea.Commercial, ProjectTaskType.PresentQuotationToClient, "advisor", now.AddDays(-1)); present.Start(now.AddDays(-1), now.AddDays(1)); present.Complete(now);
            var decision = new ClientQuotationDecision(project.Id, quote.Id, present.Id, ClientQuotationDecisionType.Approved, null, now, "advisor", null, null);
            var accounting = new ProjectTask(project.Id, OperationalArea.Accounting, ProjectTaskType.RegisterInitialDeposit, "accountant", now); accounting.StartUntracked(now); accounting.Complete(now);
            var handoff = new AccountingHandoff(project.Id, decision.Id, quote.Id, 1, accounting.Id, now, "advisor", null, null);
            var task = new ProjectTask(project.Id, OperationalArea.Installation, ProjectTaskType.TakeMeasurements, "installer", now);
            db.AddRange(version, catalog, quote, present, decision, accounting, handoff, new InitialDeposit(handoff, 50, now, now, null, null, "accountant", null, null), task);

            task.StartUntracked(now);
            var checklist = new MeasurementChecklist(project.Id, quote.Id, 1, task.Id, now, actor.Subject);
            db.Add(checklist);

        var row = MeasurementStructure.Added(checklist, "Legacy", 2, 2.4m, "Historical note", now, actor.Subject);
        row.RecordActual(2, 2.4m, "Legacy site system", now, actor.Subject); row.Measure(true, now, actor.Subject);
        checklist.Submit(now, actor.Subject, "{\"legacy\":true}"); db.Add(row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync("20261104150914_AddInstallationActualMeasurements");
        var before = await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(s)::text AS \"Value\" FROM \"MeasurementStructures\" s").ToArrayAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var after = await db.Database.SqlQueryRaw<string>("SELECT (to_jsonb(s) - 'TechnicalJson' - 'SketchVersionId' - 'MeasurementType' - 'ActualLocation' - 'InstallationObservations' - 'ComparisonJson' - 'ModificationJson')::text AS \"Value\" FROM \"MeasurementStructures\" s").ToArrayAsync();
        Assert.Equal(before, after);
        var restored = await db.MeasurementStructures.SingleAsync(); Assert.Equal(row.Id, restored.Id); Assert.NotNull(restored.MeasuredAt);
        Assert.Null(restored.Technical); Assert.Null(restored.SketchVersionId); Assert.Equal(2, restored.ActualWidth); Assert.Equal(2.4m, restored.ActualHeight);
        Assert.Equal("{\"legacy\": true}", (await db.MeasurementChecklists.SingleAsync()).SnapshotJson);
    }
}
