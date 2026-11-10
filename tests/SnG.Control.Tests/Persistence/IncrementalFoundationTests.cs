
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SnG.Control.Application.Features.Measurements;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;

namespace SnG.Control.Tests.Persistence;

public sealed class IncrementalFoundationTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private const string Previous = "20261105182823_AddOpeningRecordRounds";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void DomainRejectsInventedProvenanceInvalidHashesAndInvalidAmendmentVersions()
    {
        var id=Guid.NewGuid(); var hash=IncrementalEvidence.Hash("{}");
        Assert.Throws<InvalidOperationException>(()=>IncrementalEvidence.RequireComplete(IncrementalEvidence.LegacyIncomplete));
        Assert.Throws<ArgumentException>(()=>new MeasurementSubmission(id,id,id,1,Now,"synthetic",IncrementalEvidence.LegacyIncomplete,"MISSING_MEMBERS","ABSENT","{}",hash,false,Guid.NewGuid(),hash));
        Assert.Throws<ArgumentException>(()=>new MeasurementSubmissionMember(id,id,id,2,null,0,"{}",hash));
        Assert.Throws<ArgumentException>(()=>new MeasurementSubmissionMember(id,id,id,1,null,0,"{}",new string('0',64)));
        Assert.Throws<ArgumentException>(()=>new MeasurementAmendment(id,id,1,"reason",Now,"synthetic",Guid.NewGuid(),hash));
        Assert.Throws<ArgumentException>(()=>new WorkPackageAssignment(id,id,id,2,"synthetic",Now,null,null));
        Assert.Throws<ArgumentException>(()=>new SubmissionReport(id,id,1,null,null,hash,hash));
        var valid = new MeasurementSubmission(id,id,id,1,Now,"synthetic",IncrementalEvidence.Complete,null,"ABSENT","{}",hash,true,null,null);
        Assert.Equal("ABSENT",valid.ReportAvailability);
        Assert.Equal(IncrementalEvidence.Complete,valid.EvidenceStatus);
    }

    [VerificationPostgresFact]
    public async Task UpgradeClassifiesFrozenEvidenceAndPreservesEveryLegacyRowAndDocument()
    {
        await using var db=await Database();
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        var projectOnly=await Seed(db,0);
        var unsubmitted=await Seed(db,1);
        var noPdf=await Seed(db,2);
        var engineering=await Seed(db,3);
        var confirmed=await Seed(db,4);
        var rounds=await Seed(db,5);
        var accepted=await Seed(db,6);
        var incomplete=await Seed(db,2, "{\"legacy\":true}");
        var empty=await Seed(db,2, "{\"Structures\":[],\"DocumentIds\":[]}");
        var malformed=await Seed(db,2, "{\"Structures\":{},\"DocumentIds\":[]}");
        var before=await LegacyRows(db);
        var reportsBefore=await db.MeasurementReports.AsNoTracking().ToArrayAsync();

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(before,await LegacyRows(db));
        Assert.False(await db.MeasurementChecklists.AnyAsync(c=>c.IsIncremental));
        Assert.False(await db.Set<MeasurementSubmission>().AnyAsync(s=>s.ProjectId==projectOnly.Project.Id||s.ProjectId==unsubmitted.Project.Id));
        var complete=await db.Set<MeasurementSubmission>().SingleAsync(s=>s.Id==noPdf.Checklist!.SubmissionId);
        Assert.Equal(IncrementalEvidence.Complete,complete.EvidenceStatus);
        Assert.Equal("ABSENT",complete.ReportAvailability); Assert.Null(complete.ScopeIssue);
        Assert.Equal(2,await db.Set<MeasurementSubmissionMember>().CountAsync(s=>s.SubmissionId==complete.Id));
        foreach(var (seed,reason) in new[]{(incomplete,"MISSING_MEMBERS"),(empty,"EMPTY_SCOPE"),(malformed,"MALFORMED_MEMBERS")})
        {
            var row=await db.Set<MeasurementSubmission>().SingleAsync(s=>s.Id==seed.Checklist!.SubmissionId);
            Assert.Equal(IncrementalEvidence.LegacyIncomplete,row.EvidenceStatus); Assert.Equal(reason,row.ScopeIssue);
            Assert.Equal("ABSENT",row.ReportAvailability);
            Assert.Empty(await db.Set<MeasurementSubmissionMember>().Where(s=>s.SubmissionId==row.Id).ToArrayAsync());
            await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "EngineeringWorkspaces" ("ProjectId","CreatedAt") VALUES ({row.ProjectId},{Now});
                INSERT INTO "EngineeringReceipts" ("ProjectId","SubmissionId","ReceivedAt") VALUES ({row.ProjectId},{row.Id},{Now});
                """));
            await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "MeasurementSubmissions" SET "EvidenceStatus"='COMPLETE' WHERE "Id"={row.Id}"""));
        }
        foreach(var report in reportsBefore)
        {
            var link=await db.Set<SubmissionReport>().SingleAsync(s=>s.SubmissionId==report.SubmissionId);
            Assert.Equal(report.ChecklistId,link.LegacyChecklistId); Assert.Null(link.Pdf);
            var restored=await db.MeasurementReports.AsNoTracking().SingleAsync(r=>r.ChecklistId==report.ChecklistId);
            Assert.Equal(report.Pdf,restored.Pdf); Assert.Equal(report.PdfSha256,restored.PdfSha256);
            Assert.Equal(report.SnapshotCanonical,restored.SnapshotCanonical); Assert.Equal(report.SnapshotSha256,restored.SnapshotSha256);
        }
        Assert.Equal(4,await db.Set<OpeningRecord>().CountAsync());
        var acceptedActa=await db.Set<OpeningRecord>().SingleAsync(o=>o.ProjectId==accepted.Project.Id);
        var acceptance=await db.Set<OpeningAcceptance>().SingleAsync(a=>a.ProjectId==accepted.Project.Id);
        Assert.Equal(accepted.LastRound!.Id,acceptance.Id);
        Assert.Equal(accepted.LastRound.SignedDocumentId,acceptance.SignedDocumentId);
        Assert.Equal(2,await db.Set<OpeningAcceptanceMember>().CountAsync(m=>m.AcceptanceId==acceptance.Id));
        Assert.Empty(await db.Set<ActiveOpeningRecordMember>().Where(m=>m.OpeningRecordId==acceptedActa.Id).ToArrayAsync());
        Assert.Equal(accepted.LastRound.FilingTaskId,(await db.Set<OpeningFilingDelivery>().SingleAsync()).TaskId);
        Assert.Equal(2,await db.Set<OpeningRoundLink>().CountAsync(l=>l.ProjectId==rounds.Project.Id));
        Assert.Equal(2,await db.Set<EngineeringEvidenceCheck>().CountAsync(c=>c.ProjectId==engineering.Project.Id));
        Assert.All(await db.Set<EngineeringEvidenceCheck>().Where(c=>c.ProjectId==confirmed.Project.Id).ToArrayAsync(),c=>Assert.True(c.Completed));

        // Replaying the compatibility projection cannot promote legacy evidence or duplicate history.
        foreach(var p in new[]{noPdf,engineering,confirmed,rounds,accepted,incomplete,empty,malformed})
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT sng_incremental_legacy_project({p.Project.Id})");
        Assert.Equal(before,await LegacyRows(db));
        Assert.False(await db.MeasurementChecklists.AnyAsync(c=>c.IsIncremental));
        Assert.Single(await db.Set<OpeningAcceptance>().ToArrayAsync());
        Assert.Equal(7,await db.Set<MeasurementSubmission>().CountAsync(s=>s.ProjectId!=malformed.Project.Id));
    }

    [VerificationPostgresFact]
    public async Task ConstraintsProtectScopeIdentityAssignmentsImmutabilityAndAtomicRollback()
    {
        await using var db=await Database(); await db.GetService<IMigrator>().MigrateAsync(Previous);
        var a=await Seed(db,3); var other=await Seed(db,3); await db.Database.MigrateAsync();
        var sub=a.Checklist!.SubmissionId!.Value; var project=a.Project.Id;
        var acta=await db.Set<OpeningRecord>().SingleAsync(x=>x.ProjectId==project);
        var member=await db.Set<OpeningRecordMember>().FirstAsync(x=>x.ProjectId==project);
        var newActa=Guid.NewGuid();
        db.Add(new OpeningRecord(newActa,project,2,Now,null,IncrementalEvidence.Complete));
        await db.SaveChangesAsync();
        db.Add(new OpeningRecordMember(newActa,project,member.StructureId,sub,1,0)); await db.SaveChangesAsync();
        await Reject(db,"23505",()=>db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "ActiveOpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId") VALUES ({newActa},{project},{member.StructureId})"""));
        await Reject(db,"23503",()=>db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "OpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId","SubmissionId","EvidenceVersion","Position") VALUES ({newActa},{project},{other.Rows[0].Id},{sub},1,1)"""));
        await Reject(db,"23505",()=>db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "OpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId","SubmissionId","EvidenceVersion","Position") VALUES ({newActa},{project},{member.StructureId},{sub},1,1)"""));
        var assignment=await db.Set<WorkPackageAssignment>().FirstAsync(x=>x.ProjectId==project&&x.EndedAt==null);
        await Reject(db,"23505",()=>db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "WorkPackageAssignments" ("Id","ProjectId","PackageId","Area","Subject","AssignedAt") VALUES ({Guid.NewGuid()},{project},{assignment.PackageId},{assignment.Area},'another-synthetic',{Now})"""));
        await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "MeasurementSubmissionMembers" SET "SnapshotCanonical"={ "{}" } WHERE "SubmissionId"={sub}"""));
        await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "SubmissionReports" WHERE "SubmissionId"={sub}"""));
        var rollbackId=Guid.NewGuid();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "OpeningRecords" ("Id","ProjectId","Number","CreatedAt","EvidenceStatus") VALUES ({rollbackId},{project},3,{Now},'COMPLETE')""");
            await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO "ActiveOpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId") VALUES ({newActa},{project},{member.StructureId})"""));
            await tx.RollbackAsync();
        }
        Assert.False(await db.Set<OpeningRecord>().AnyAsync(x=>x.Id==rollbackId));
        // The legacy consumer guardrails remain present.
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname=current_schema() AND indexname='UX_ProjectTasks_FileOpeningRecord') AS \"Value\"").SingleAsync());
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname=current_schema() AND indexname='UX_OpeningRounds_Active') AS \"Value\"").SingleAsync());
    }


    [VerificationPostgresFact]
    public async Task NativeFoundationRepresentsMixedTranchesPartialAcceptancesAndIndependentFilingTasks()
    {
        // Seed the published Entrega 3 foundation, then prove Entrega 4 preserves it.
        await using var db=await Database(); await db.GetService<IMigrator>().MigrateAsync("20261110184921_EnableIncrementalEngineering");
        var seed=await Seed(db,1); var project=seed.Project.Id; var canonicalRows=seed.Rows.Select(MeasurementStructureView.From).ToArray();
        var submissions=new List<MeasurementSubmission>();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            db.Add(new EngineeringWorkspace(project,Now));
            for(var i=0;i<2;i++)
            {
                var json=JsonSerializer.Serialize(new{Structures=new[]{canonicalRows[i]},DocumentIds=Array.Empty<Guid>()});
                var submission=new MeasurementSubmission(Guid.NewGuid(),project,seed.Checklist!.Id,i+1,Now,"installer",IncrementalEvidence.Complete,null,"PRESENT",json,IncrementalEvidence.Hash(json),false,Guid.NewGuid(),IncrementalEvidence.Hash(json));
                byte[] pdf=Encoding.ASCII.GetBytes("%PDF-1.4 synthetic tranche "+i);
                db.Add(new SubmissionReport(submission.Id,project,1,null,pdf,Hash(pdf),IncrementalEvidence.Hash(json),json));
                var evidence=JsonSerializer.Serialize(canonicalRows[i]);submissions.Add(submission);
                db.AddRange(submission,new MeasurementSubmissionMember(submission.Id,project,seed.Rows[i].Id,1,null,0,evidence,IncrementalEvidence.Hash(evidence)),
                    new EngineeringReceipt(project,submission.Id,Now));
            }
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        Assert.Equal(2,await db.Set<MeasurementSubmission>().CountAsync(s=>s.ProjectId==project));
        Assert.Equal(2,await db.Set<SubmissionReport>().CountAsync(r=>r.ProjectId==project));
        var acta=new OpeningRecord(Guid.NewGuid(),project,1,Now,null,IncrementalEvidence.Complete);
        var round=new OpeningRoundLink(Guid.NewGuid(),project,acta.Id,1);
        db.AddRange(acta,round);
        for(var i=0;i<2;i++)db.Add(new OpeningRecordMember(acta.Id,project,seed.Rows[i].Id,submissions[i].Id,1,i));
        await db.SaveChangesAsync();
        foreach(var row in seed.Rows)db.Add(new ActiveOpeningRecordMember(acta.Id,project,row.Id));
        await db.SaveChangesAsync();
        var presented=Document(project);var signed1=Document(project);var signed2=Document(project);var hash=IncrementalEvidence.Hash("synthetic");
        db.AddRange(presented,signed1,signed2,
            new OpeningDocumentScope(acta.Id,project,presented.Id,"ENGINEERING",1,hash,"Explicit two-structure scope"),
            new OpeningDocumentScope(acta.Id,project,signed1.Id,"SIGNED",1,hash,"Explicit first-structure scope"),
            new OpeningDocumentScope(acta.Id,project,signed2.Id,"SIGNED",2,hash,"Explicit second-structure scope"));
        for(var i=0;i<2;i++)db.Add(new OpeningDocumentMember(acta.Id,project,presented.Id,seed.Rows[i].Id));
        db.AddRange(new OpeningDocumentMember(acta.Id,project,signed1.Id,seed.Rows[0].Id),new OpeningDocumentMember(acta.Id,project,signed2.Id,seed.Rows[1].Id));
        await db.SaveChangesAsync();
        var signed=new[]{signed1,signed2};var acceptances=new List<OpeningAcceptance>();
        for(var i=0;i<2;i++)
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            var acceptance=new OpeningAcceptance(Guid.NewGuid(),project,acta.Id,round.RoundId,presented.Id,signed[i].Id,Now,"advisor",false,Guid.NewGuid(),hash);
            acceptances.Add(acceptance);
            var task=ProjectTask.ForOpeningAcceptance(acceptance,"engineer",Now);
            db.AddRange(acceptance,new OpeningAcceptanceMember(acceptance.Id,project,acta.Id,seed.Rows[i].Id),
                task,new OpeningFilingDelivery(Guid.NewGuid(),project,acceptance.Id,task.Id));
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        Assert.Equal(2,await db.Set<OpeningAcceptance>().CountAsync(x=>x.ProjectId==project));
        var tasks=await db.ProjectTasks.Where(t=>t.ProjectId==project&&t.Type==ProjectTaskType.FileOpeningRecord).ToArrayAsync();
        Assert.Equal(2,tasks.Length);Assert.All(tasks,t=>{Assert.Equal(ProjectTaskStatus.Pending,t.Status);Assert.Null(t.StartedAt);Assert.Null(t.DeadlineAt);});
        Assert.Equal(2,await db.Set<OpeningFilingDelivery>().CountAsync(f=>f.ProjectId==project));
        Assert.Equal(2,await db.Set<OpeningAcceptanceMember>().CountAsync(m=>m.ProjectId==project));
        await Reject(db,"23505",()=>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OpeningAcceptances" ("Id","ProjectId","OpeningRecordId","RoundId","PresentedDocumentId","SignedDocumentId","At","ActorSubject","IsLegacy","RequestKey","Fingerprint")
            VALUES ({Guid.NewGuid()},{project},{acta.Id},{round.RoundId},{presented.Id},{signed1.Id},{Now},'advisor',false,{acceptances[0].RequestKey},{hash})
            """));
        await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OpeningAcceptanceMembers" ("AcceptanceId","ProjectId","OpeningRecordId","StructureId")
            VALUES ({acceptances[0].Id},{project},{acta.Id},{seed.Rows[1].Id})
            """));
        // An amendment adds a new evidence version while the original remains unchanged.
        var original=await db.Set<MeasurementSubmissionMember>().SingleAsync(m=>m.SubmissionId==submissions[0].Id);
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            var changed=JsonSerializer.Serialize(new{Id=seed.Rows[0].Id,Reason="Synthetic authorized correction"});
            db.AddRange(new MeasurementAmendment(submissions[0].Id,project,2,"Synthetic reason",Now,"installer",Guid.NewGuid(),hash),
                new MeasurementSubmissionMember(submissions[0].Id,project,seed.Rows[0].Id,2,2,0,changed,IncrementalEvidence.Hash(changed)));
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        Assert.Equal(original.SnapshotCanonical,(await db.Set<MeasurementSubmissionMember>().AsNoTracking().SingleAsync(m=>m.SubmissionId==submissions[0].Id&&m.EvidenceVersion==1)).SnapshotCanonical);
        Assert.Equal(2,await db.Set<MeasurementSubmissionMember>().CountAsync(m=>m.SubmissionId==submissions[0].Id));
        // Confirmed scope cannot be expanded in a later transaction, even with a valid hash.
        await Reject(db,"23514",()=>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubmissionDocumentScopes" ("SubmissionId","ProjectId","DocumentId","Declaration")
            VALUES ({submissions[0].Id},{project},{presented.Id},'Late scope change')
            """));
        var acceptedBefore=await db.Set<OpeningAcceptance>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoundId,x.SignedDocumentId,x.At}).ToArrayAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(acceptedBefore,await db.Set<OpeningAcceptance>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoundId,x.SignedDocumentId,x.At}).ToArrayAsync());
        Assert.Empty(await db.Set<CommercialOpeningRound>().Where(x=>x.ProjectId==project).ToArrayAsync());
        var countBefore=await db.Set<MeasurementSubmissionMember>().CountAsync();
        await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Equal(countBefore,await db.Set<MeasurementSubmissionMember>().CountAsync());
        // No legacy task, notification, HTTP transition or global project completion was created by the foundation.
        Assert.Equal(ProjectTaskStatus.InProgress,(await db.ProjectTasks.SingleAsync(t=>t.Id==seed.Checklist!.TaskId)).Status);
        Assert.Empty(await db.ProjectNotifications.Where(n=>n.ProjectId==project).ToArrayAsync());
    }

    private sealed class LegacySeedContext(string connection) : SnGControlDbContext(
        new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(connection).Options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            // Seed the actual pre-upgrade shape. New nullable columns do not yet exist there.
            foreach(var type in model.Model.GetEntityTypes().Where(e=>e.ClrType.Namespace=="SnG.Control.Domain.Entities.Incremental").Select(e=>e.ClrType).ToArray())
                model.Ignore(type);
            model.Entity<ProjectTask>().Ignore(t=>t.IncrementalAcceptanceId);
            model.Entity<MeasurementChecklist>().Ignore(t=>t.IsIncremental);
        }
    }


    [VerificationPostgresFact]
    public async Task IncompleteScopeWithAnExistingPdfAndSignedAcceptanceIsRetainedWithoutInventedMembers()
    {
        await using var db=await Database();await db.GetService<IMigrator>().MigrateAsync(Previous);
        var legacy=await Seed(db,6,"{\"legacy\":true}");
        var before=await LegacyRows(db);
        var pdf=await db.MeasurementReports.AsNoTracking().SingleAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(before,await LegacyRows(db));
        var submission=await db.Set<MeasurementSubmission>().SingleAsync();
        Assert.Equal(IncrementalEvidence.LegacyIncomplete,submission.EvidenceStatus);
        Assert.Equal("MISSING_MEMBERS",submission.ScopeIssue);Assert.Equal("PRESENT",submission.ReportAvailability);
        Assert.Equal(pdf.SnapshotCanonical,submission.SnapshotCanonical);
        Assert.Equal(pdf.Pdf,(await db.MeasurementReports.SingleAsync()).Pdf);
        Assert.Empty(await db.Set<MeasurementSubmissionMember>().ToArrayAsync());
        Assert.Empty(await db.Set<EngineeringReceipt>().ToArrayAsync());
        Assert.Empty(await db.Set<OpeningRecordMember>().ToArrayAsync());
        Assert.Equal(IncrementalEvidence.LegacyIncomplete,(await db.Set<OpeningRecord>().SingleAsync()).EvidenceStatus);
        Assert.Equal(legacy.LastRound!.Id,(await db.Set<OpeningAcceptance>().SingleAsync()).Id);
        Assert.Empty(await db.Set<OpeningAcceptanceMember>().ToArrayAsync());
        Assert.Equal(legacy.LastRound.FilingTaskId,(await db.Set<OpeningFilingDelivery>().SingleAsync()).TaskId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT sng_incremental_legacy_project({legacy.Project.Id})");
        Assert.Equal(before,await LegacyRows(db));
        Assert.Empty(await db.Set<MeasurementSubmissionMember>().ToArrayAsync());
    }


    private static async Task Reject(SnGControlDbContext db,string state,Func<Task> write)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(write); Assert.Equal(state,error.SqlState);
        await tx.RollbackAsync(); db.ChangeTracker.Clear();
    }
    private static async Task<SnGControlDbContext> Database()
    {
        var config=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SNG_VERIFICATION_TEST_DATABASE"));
        Assert.Equal("127.0.0.1",config.Host); Assert.Equal("verification_test",config.Database);
        var schema="incremental_"+Guid.NewGuid().ToString("N");
        await using var connection=new NpgsqlConnection(config.ConnectionString); await connection.OpenAsync();
        await using(var command=new NpgsqlCommand($"CREATE SCHEMA {schema}",connection)) await command.ExecuteNonQueryAsync();
        config.SearchPath=schema;
        return new(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql(config.ConnectionString,o=>o.MigrationsHistoryTable("__EFMigrationsHistory",schema)).Options);
    }
    private static async Task<string[]> LegacyRows(SnGControlDbContext db)
    {
        var rows=new List<string>();
        // Identifiers below are a closed literal list, never request-controlled.
#pragma warning disable EF1002
        foreach(var table in new[]{"Projects","ProjectTasks","ProjectNotifications","MeasurementChecklists","MeasurementStructures","MeasurementEvents","MeasurementSketchVersions","MeasurementSubmissionDocuments","MeasurementReports","ProjectDocuments","EngineeringChecklists","EngineeringStructures","EngineeringEvents","EngineeringActas","OpeningRecordRounds","OpeningRecordStructures","OpeningRecordDocuments","OpeningRecordEvents"})
            rows.AddRange(await db.Database.SqlQueryRaw<string>($"SELECT (to_jsonb(t) - 'IncrementalAcceptanceId' - 'IsIncremental')::text AS \"Value\" FROM \"{table}\" t ORDER BY to_jsonb(t)::text").ToArrayAsync());
#pragma warning restore EF1002
        return rows.ToArray();
    }
    private sealed record Historical(Project Project,MeasurementChecklist? Checklist,MeasurementStructure[] Rows,OpeningRecordRound? LastRound);
    private static async Task<Historical> Seed(SnGControlDbContext db,int stage,string? customSnapshot=null)
    {
        await using var legacy = new LegacySeedContext(db.Database.GetConnectionString()!); db = legacy;
        var client=new Client("Synthetic incremental"); var project=new Project(client.Id,"Synthetic","City",ProductLine.Classic,"advisor");
        db.AddRange(client,project); await db.SaveChangesAsync();
        if(stage==0)return new(project,null,[],null);
        var version=new BudgetQuotationVersion(project.Id,1,"synthetic.xlsx","Client","Project","City","Line","Line","Author","SG9001");
        var catalog=new CommercialCatalog(version.Id); var quotation=new CommercialQuotation(project.Id,version.Id,catalog.Id,"synthetic.xlsx",100,"head",null,null);
        var task=new ProjectTask(project.Id,OperationalArea.Installation,ProjectTaskType.TakeMeasurements,"installer",Now); task.StartUntracked(Now);
        var checklist=new MeasurementChecklist(project.Id,quotation.Id,1,task.Id,Now,"installer");
        var rows=Enumerable.Range(0,2).Select(i=>MeasurementStructure.Added(checklist,"Synthetic "+i,2,2,null,Now,"installer")).ToArray();
        db.AddRange(version,catalog,quotation,task,checklist); db.AddRange(rows);
        foreach(var row in rows)row.Measure(true,Now,"installer");
        if(stage==1){await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(project,checklist,rows,null);}
        var snapshot=new MeasurementSnapshot(quotation.Id,1,MeasurementSummary.From(rows),rows.Select(MeasurementStructureView.From).ToArray(),[]);
        var canonical=customSnapshot??JsonSerializer.Serialize(snapshot);
        checklist.Submit(Now,"installer",canonical); task.Complete(Now);
        if(stage==2){await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(project,checklist,rows,null);}
        byte[] pdf=Encoding.ASCII.GetBytes("%PDF-1.4\nsynthetic\n%%EOF");
        db.Add(new MeasurementReport(checklist,pdf,Hash(pdf),IncrementalEvidence.Hash(canonical)));
        var et=new ProjectTask(project.Id,OperationalArea.Engineering,ProjectTaskType.PrepareOpeningRecord,"engineer",Now); et.StartUntracked(Now);
        var ec=new EngineeringChecklist(project.Id,checklist.Id,checklist.SubmissionId!.Value,et.Id,Now);
        var er=rows.Select((s,i)=>new EngineeringStructure(ec,s.Id,i,JsonSerializer.Serialize(MeasurementStructureView.From(s)))).ToArray();
        db.AddRange(et,ec); db.AddRange(er);
        if(stage==3){await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(project,checklist,rows,null);}
        var doc=Document(project.Id); db.AddRange(doc,new EngineeringActa(ec,doc,1,Hash(pdf)));
        foreach(var row in er)row.SetCompleted(true,Now,"engineer");
        ec.SetDocument(doc.Id); ec.Complete(Now,"engineer",JsonSerializer.Serialize(new{DocumentId=doc.Id,Structures=er})); et.Complete(Now);
        if(stage==4){await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(project,checklist,rows,null);}
        var present=new ProjectTask(project.Id,OperationalArea.Commercial,ProjectTaskType.PresentOpeningRecord,"advisor",Now);present.StartUntracked(Now);
        var round=new OpeningRecordRound(ec,1,present.Id,doc.Id,Now); round.Present(Now,"advisor");
        var members=rows.Select(r=>new OpeningRecordStructure(round.Id,r.Id)).ToArray();
        var review=new ProjectTask(project.Id,OperationalArea.Engineering,ProjectTaskType.ReviewOpeningRecord,"engineer",Now);review.StartUntracked(Now);
        round.RequestChanges(Now,"advisor",review.Id,"Synthetic observation"); present.Complete(Now);
        round.DeclareAffected();
        var revised=Document(project.Id);round.SetDocument(revised.Id,OpeningDocumentKind.Revised);
        db.AddRange(present,round,review,revised,new OpeningRecordDocument(round,revised,OpeningDocumentKind.Revised,1,Hash(pdf),JsonSerializer.Serialize(rows.Select(r=>r.Id))));
        db.AddRange(members);
        round.CompleteReview(Now,"engineer",JsonSerializer.Serialize(new{Structures=members,DocumentId=revised.Id})); review.Complete(Now);
        var present2=new ProjectTask(project.Id,OperationalArea.Commercial,ProjectTaskType.PresentOpeningRecord,"advisor",Now);present2.StartUntracked(Now);
        var round2=new OpeningRecordRound(ec,2,present2.Id,revised.Id,Now);
        var members2=rows.Select(r=>new OpeningRecordStructure(round2.Id,r.Id)).ToArray();db.AddRange(present2,round2);db.AddRange(members2);
        if(stage==6)
        {
            round2.Present(Now,"advisor");
            foreach(var row in members2)row.Confirm(round2,true);
            var signed=Document(project.Id);round2.SetDocument(signed.Id,OpeningDocumentKind.Signed);
            var filing=new ProjectTask(project.Id,OperationalArea.Engineering,ProjectTaskType.FileOpeningRecord,"engineer",Now);
            round2.Accept(Now,"advisor",filing.Id,JsonSerializer.Serialize(new{Structures=members2,SignedDocumentId=signed.Id}));present2.Complete(Now);
            db.AddRange(signed,new OpeningRecordDocument(round2,signed,OpeningDocumentKind.Signed,1,Hash(pdf),"[]"),filing);
        }
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(project,checklist,rows,round2);
    }
    private static ProjectDocument Document(Guid project)=>new(Guid.NewGuid(),project,"synthetic.pdf","application/pdf",100,Guid.NewGuid(),Now,"engineer",null,null,isEngineeringDocument:true);
}
