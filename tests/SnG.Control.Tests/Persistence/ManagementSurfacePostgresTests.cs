using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Persistence;
public class ManagementSurfacePostgresTests
{
    [Fact]
    public async Task IsolatedPostgres_CurrentVersionsExactTotalsPhysicalUnitsAndPaging()
    {
        var raw=Environment.GetEnvironmentVariable("SNG_G21_ADMIN");
        if(string.IsNullOrEmpty(raw)) return; // Opt-in only; never uses application configuration.
        var settings=new NpgsqlConnectionStringBuilder(raw);
        Assert.Contains(settings.Host,new[]{"localhost","127.0.0.1"});Assert.Equal(5432,settings.Port);Assert.Equal("postgres",settings.Database);
        settings.Pooling=false;
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        await using(var identity=new NpgsqlCommand("SELECT current_database()",admin))Assert.Equal("postgres",await identity.ExecuteScalarAsync());
        var name="sng_g21_temp_"+Guid.NewGuid().ToString("N");
        await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{name}\"",admin))await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database=name;Assert.StartsWith("sng_g21_temp_",settings.Database);
            await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();
            await using(var identity=new NpgsqlCommand("SELECT current_database()",connection))Assert.Equal(name,await identity.ExecuteScalarAsync());
            // Minimal synthetic schema for REAL query execution; no operational migrations or documents.
            await using(var setup=new NpgsqlCommand("""
                CREATE TABLE "Clients" ("Id" uuid PRIMARY KEY,"Name" text);
                CREATE TABLE "Projects" ("Id" uuid PRIMARY KEY,"ClientId" uuid,"Name" text,"City" text,"SgCode" text,"ProductLine" integer);
                CREATE TABLE "ProjectTasks" ("Id" uuid PRIMARY KEY,"ProjectId" uuid,"Area" integer,"Status" integer);
                CREATE TABLE "BudgetQuotationVersions" ("Id" uuid PRIMARY KEY,"VersionNumber" integer);
                CREATE TABLE "CommercialQuotations" ("Id" uuid PRIMARY KEY,"ProjectId" uuid,"BudgetQuotationVersionId" uuid);
                CREATE TABLE "CommercialQuotationItems" ("Id" uuid PRIMARY KEY,"CommercialQuotationId" uuid,"SquareMeters" numeric,"Quantity" integer);
                CREATE TABLE "MeasurementStructures" ("Id" uuid PRIMARY KEY,"ProjectId" uuid,"ActualWidth" numeric,"ActualHeight" numeric,"MeasuredAt" timestamptz,"RetiredAt" timestamptz);
                CREATE TABLE "MeasurementSubmissionMembers" ("ProjectId" uuid,"StructureId" uuid,"SubmissionId" uuid,"EvidenceVersion" integer);
                INSERT INTO "Clients" VALUES ('00000000-0000-4000-8000-000000000001','Synthetic client');
                INSERT INTO "Projects" VALUES ('00000000-0000-4000-8000-000000000002','00000000-0000-4000-8000-000000000001','Synthetic project','City','SG001',1),('00000000-0000-4000-8000-000000000003','00000000-0000-4000-8000-000000000001','No quotation','City',null,1);
                INSERT INTO "ProjectTasks" VALUES ('00000000-0000-4000-8000-000000000004','00000000-0000-4000-8000-000000000002',5,1),('00000000-0000-4000-8000-000000000005','00000000-0000-4000-8000-000000000002',5,1);
                INSERT INTO "BudgetQuotationVersions" VALUES ('00000000-0000-4000-8000-000000000006',1),('00000000-0000-4000-8000-000000000007',2);
                INSERT INTO "CommercialQuotations" VALUES ('00000000-0000-4000-8000-000000000008','00000000-0000-4000-8000-000000000002','00000000-0000-4000-8000-000000000006'),('00000000-0000-4000-8000-000000000009','00000000-0000-4000-8000-000000000002','00000000-0000-4000-8000-000000000007');
                INSERT INTO "CommercialQuotationItems" VALUES ('00000000-0000-4000-8000-000000000010','00000000-0000-4000-8000-000000000008',999,5),('00000000-0000-4000-8000-000000000011','00000000-0000-4000-8000-000000000009',12.3456789,3),('00000000-0000-4000-8000-000000000012','00000000-0000-4000-8000-000000000009',-1,1);
                INSERT INTO "MeasurementStructures" VALUES ('00000000-0000-4000-8000-000000000013','00000000-0000-4000-8000-000000000002',1.35,1.86,now(),null),('00000000-0000-4000-8000-000000000014','00000000-0000-4000-8000-000000000002',null,null,null,null),('00000000-0000-4000-8000-000000000015','00000000-0000-4000-8000-000000000002',2,3,now(),now());
                INSERT INTO "MeasurementSubmissionMembers" VALUES ('00000000-0000-4000-8000-000000000002','00000000-0000-4000-8000-000000000013','00000000-0000-4000-8000-000000000016',1),('00000000-0000-4000-8000-000000000002','00000000-0000-4000-8000-000000000013','00000000-0000-4000-8000-000000000016',2);
                """,connection))await setup.ExecuteNonQueryAsync();
            await using var db=new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(settings.ConnectionString).Options);
            var store=new EfManagementSurfaceStore(db,TimeProvider.System);
            var all=await store.ReadAsync(new(),default);
            Assert.Equal(2,all.TotalProjects);Assert.Equal("12.3456789",all.Quoted.Value);Assert.Equal(1,all.Quoted.Excluded);
            Assert.Equal("2.511",all.Measured.Value);Assert.Equal(1,all.Measured.Eligible);Assert.Equal(2,all.Measured.Excluded);
            Assert.Equal(1,all.Filed.Excluded);Assert.Null(all.Filed.Value);
            Assert.Equal(all.Quoted.Value,all.Projects[0].QuotedSquareMeters);Assert.Equal(all.Measured.Value,all.Projects[0].ReferenceSquareMeters);
            Assert.Null(all.Projects[1].QuotedSquareMeters);Assert.Empty(db.ChangeTracker.Entries());
            var filtered=await store.ReadAsync(new(Area:OperationalArea.Engineering,Status:ProjectTaskStatus.Pending),default);
            Assert.Equal(1,filtered.TotalProjects);Assert.Equal(all.Quoted.Value,filtered.Quoted.Value);
            var empty=await store.ReadAsync(new(Area:OperationalArea.Engineering,Status:ProjectTaskStatus.Completed),default);Assert.Equal(0,empty.TotalProjects);Assert.Null(empty.Quoted.Value);
            Assert.Empty((await store.ReadAsync(new(Skip:50),default)).Projects);
        }
        finally
        {
            Assert.Equal("postgres",admin.Database);Assert.Matches("^sng_g21_temp_[a-f0-9]{32}$",name);
            await using var drop=new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
            await using var verify=new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname=@name",admin);verify.Parameters.AddWithValue("name",name);Assert.Equal(0L,await verify.ExecuteScalarAsync());
        }
    }
}
