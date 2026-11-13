using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Tests.Persistence;
internal static class ProductionPreparationHttpChecks
{
    public static async Task Verify(IncrementalHttpHarness h)
    {
        await using var db=h.Db();
        await VerifyMigration(db);
        var releases=await db.Set<OpeningFilingRelease>().AsNoTracking().OrderBy(x=>x.At).ToArrayAsync(); Assert.Equal(2,releases.Length);
        var hashes=releases.Select(r=>r.Sha256).ToArray();var tasks=await db.ProjectTasks.CountAsync();var documents=await db.ProjectDocuments.CountAsync();
        Assert.Empty(await db.Set<ProductionPreparation>().ToArrayAsync());
        var available=await h.Get<ReleaseSummary[]>("operations","OperationsHead","operations/releases"); Assert.Equal(2,available.Length);
        Assert.Empty(await db.Set<ProductionPreparation>().ToArrayAsync()); // GET cannot discover by creating.
        foreach(var role in new[]{("worker","Engineering"),("management","Management"),("outsider","OperationsHead"),("operations","Production")})
        {
            using var denied=await h.Send(role.Item1,role.Item2,HttpMethod.Post,$"operations/releases/{releases[0].VersionId}/preparation");await IncrementalHttpHarness.Status(denied,403);
        }
        using(var denied=await h.Send("","",HttpMethod.Get,"operations/preparations"))await IncrementalHttpHarness.Status(denied,401);
        using(var missing=await h.Send("operations","OperationsHead",HttpMethod.Post,$"operations/releases/{Guid.NewGuid()}/preparation"))await IncrementalHttpHarness.Status(missing,404);
        var draft=await db.Set<OpeningFilingVersion>().AsNoTracking().FirstAsync(v=>v.ConfirmedAt==null);
        using(var notConfirmed=await h.Send("operations","OperationsHead",HttpMethod.Post,$"operations/releases/{draft.Id}/preparation"))await IncrementalHttpHarness.Status(notConfirmed,404);
        async Task Create(Guid release) { using var response=await h.Send("operations","OperationsHead",HttpMethod.Post,$"operations/releases/{release}/preparation");await IncrementalHttpHarness.Status(response,200); }
        // A direct write cannot commit a preparation with an omitted frozen scope.
        await using(var incomplete=h.Db())
        {
            await using var tx=await incomplete.Database.BeginTransactionAsync();
            incomplete.Add(new ProductionPreparation(releases[1],h.Clock.Now,"operations"));
            await incomplete.SaveChangesAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>incomplete.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"));
            Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        await Task.WhenAll(Enumerable.Range(0,5).Select(_=>Create(releases[0].VersionId)));
        var preparation=Assert.Single(await db.Set<ProductionPreparation>().ToArrayAsync());
        await Create(releases[1].VersionId);Assert.Equal(2,await db.Set<ProductionPreparation>().CountAsync());
        var detail=await h.Get<PreparationDetail>("operations","OperationsHead",$"operations/preparations/{preparation.Id}");
        Assert.Equal("PendingExtraction",detail.Preparation.State);Assert.Equal(releases[0].Sha256,detail.Preparation.ManifestSha256);
        Assert.Equal(await db.Set<OpeningFilingMember>().CountAsync(m=>m.VersionId==preparation.ReleaseVersionId),detail.Members.Count);
        Assert.Equal(await db.Set<OpeningFilingDocument>().CountAsync(d=>d.VersionId==preparation.ReleaseVersionId&&d.State=="Current"),detail.Documents.Count);
        async Task Reject(FormattableString sql,string code="23514")
        { await using var c=h.Db();await using var tx=await c.Database.BeginTransactionAsync();var e=await Assert.ThrowsAsync<PostgresException>(()=>c.Database.ExecuteSqlInterpolatedAsync(sql));Assert.Equal(code,e.SqlState);await tx.RollbackAsync(); }
        await Reject($"UPDATE \"ProductionPreparations\" SET \"State\"=2 WHERE \"Id\"={preparation.Id}");
        await Reject($"UPDATE \"PreparationSourceMembers\" SET \"Op\"='FOREIGN' WHERE \"PreparationId\"={preparation.Id}");
        await Reject($"UPDATE \"OpeningFilingVersions\" SET \"ManifestSha256\"='changed' WHERE \"Id\"={preparation.ReleaseVersionId}");
        var rev=new ProductionComponentRevision(preparation,1,h.Clock.Now,"operations");db.Add(rev);await db.SaveChangesAsync();
        var member=await db.Set<PreparationSourceMember>().FirstAsync(x=>x.PreparationId==preparation.Id);
        var doc=await db.Set<PreparationSourceDocument>().FirstAsync(x=>x.PreparationId==preparation.Id);
        var tech=new CandidateTechnicalData(ComponentMaterial.MicroperforatedSheet,"lamina",null,null,"975 x 384",null,null,975,384,null,null,null,1,null,"{}","{}");
        var origin=new CandidateOrigin(1,"19",null,"{}","synthetic-v1","synthetic-v1");
        var row=new ProductionCandidateRow(rev,member,doc,tech,origin);var identical=new ProductionCandidateRow(rev,member,doc,tech,origin);
        db.AddRange(row,identical);await db.SaveChangesAsync();Assert.NotEqual(row.Id,identical.Id);Assert.Equal(FabricationRoute.Aluminum,row.ProposedRoute);
        await Reject($"UPDATE \"ProductionCandidateRows\" SET \"Op\"='FOREIGN' WHERE \"Id\"={row.Id}","23514");
        await Reject($"UPDATE \"ProductionCandidateRows\" SET \"DocumentSha256\"={new string('0',64)} WHERE \"Id\"={row.Id}","23514");
        await Reject($"UPDATE \"ProductionCandidateRows\" SET \"StructureId\"={Guid.NewGuid()} WHERE \"Id\"={row.Id}","23514");
        await Reject($"UPDATE \"ProductionCandidateRows\" SET \"ProposedRoute\"=1 WHERE \"Id\"={row.Id}");
        await Reject($"UPDATE \"ProductionCandidateRows\" SET \"ProposedRoute\"=NULL WHERE \"Id\"={row.Id}");
        // Original rows are now immutable; assert FK enforcement separately on a new invalid insertion.
        await using(var invalid=h.Db())
        {
            var foreign=new ProductionCandidateRow(rev,member,doc,tech,origin);invalid.Add(foreign);invalid.Entry(foreign).Property(x=>x.Op).CurrentValue="FOREIGN";
            var violation=await Assert.ThrowsAsync<DbUpdateException>(()=>invalid.SaveChangesAsync());Assert.Equal("23503",Assert.IsType<PostgresException>(violation.InnerException).SqlState);
        }
        Assert.Equal(tasks,await db.ProjectTasks.CountAsync());Assert.Equal(documents,await db.ProjectDocuments.CountAsync());
        Assert.Equal(hashes,(await db.Set<OpeningFilingRelease>().AsNoTracking().OrderBy(x=>x.At).ToArrayAsync()).Select(r=>r.Sha256));
        Assert.DoesNotContain(db.Model.GetEntityTypes(),e=>e.ClrType.Name.Contains("PhysicalUnit"));
        await DocumentExtractionHttpChecks.Verify(h);
    }
    private static async Task VerifyMigration(SnG.Control.Infrastructure.Persistence.SnGControlDbContext db)
    {
        // Real historical synthetic workflow already exists; P2.1 must not backfill or rewrite it.
        var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("AddProductionHumanReview",migrations[^1]);
        async Task<string[]> Fingerprint()
        {
            var connection=(NpgsqlConnection)db.Database.GetDbConnection();
            if(connection.State!=System.Data.ConnectionState.Open)await connection.OpenAsync();
            var tables=new List<string>();
            await using(var q=new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname=current_schema() AND tablename NOT IN ('__EFMigrationsHistory','ProductionPreparations','PreparationSourceMembers','PreparationSourceDocuments','ProductionComponentRevisions','ProductionCandidateRows','DocumentExtractionJobs','ProductionReviews','ProductionReviewRows','ProductionReviewIssues','ProductionReviewEvents','ProductionApprovals','ProductionReviewAttachments') ORDER BY tablename",connection))
            await using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())tables.Add(r.GetString(0));
            var result=new List<string>();
            foreach(var table in tables)
            {
                var identifier=new NpgsqlCommandBuilder().QuoteIdentifier(table);
                await using var q=new NpgsqlCommand($"SELECT coalesce(string_agg(row_to_json(t)::text, '|' ORDER BY row_to_json(t)::text),'') FROM {identifier} t",connection);
                result.Add(table+":"+(string)(await q.ExecuteScalarAsync())!);
            }
            await using(var q=new NpgsqlCommand("SELECT pg_get_triggerdef(oid) FROM pg_trigger WHERE tgrelid IN (SELECT oid FROM pg_class WHERE relnamespace=current_schema()::regnamespace) AND NOT tgisinternal AND tgname NOT LIKE 'preparation_%' AND tgname NOT LIKE 'review_%' AND tgname NOT LIKE 'approval_%' AND tgname NOT LIKE 'extraction_%' ORDER BY oid",connection))
            await using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())result.Add(r.GetString(0));
            return result.ToArray();
        }
        var before=await Fingerprint();
        await db.GetService<IMigrator>().MigrateAsync(migrations[^4]);
        Assert.Equal(before,await Fingerprint());
        await db.Database.MigrateAsync();
        Assert.Equal(before,await Fingerprint());
        Assert.Empty(await db.Set<ProductionPreparation>().ToArrayAsync());
    }

}
