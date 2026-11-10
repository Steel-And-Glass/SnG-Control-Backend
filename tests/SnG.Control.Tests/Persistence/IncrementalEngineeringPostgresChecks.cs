using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Tests.Persistence;
internal static class IncrementalEngineeringPostgresChecks
{
    private sealed class Team:IEngineeringAssigneeIdentity,IRoleResolver { public IReadOnlyList<SystemRole> Resolve(string subject)=>subject=="advisor"?[SystemRole.CommercialAdvisor]:Subjects.Contains(subject)?[SystemRole.Engineering]:[]; public string Subject=>"engineer";public IReadOnlyList<string> Subjects=>["engineer","worker-two"]; }
    public static async Task VerifyAsync(Func<SnGControlDbContext> open,Guid id,IStreamingFileStorage storage,TaskTiming timing,byte[] pdf)
    {
        EfEngineeringStore Store(SnGControlDbContext db)=>new(db,timing,new(db,new ProjectNotificationPlanner(timing)),storage,new Team(),new Team());
        var actor=new DecisionActor("engineer","engineer@example.test","Engineer");var worker=actor with{Subject="worker-two",Email="worker@example.test"};
        async Task<EngineeringState> Read(DecisionActor? a=null) {await using var db=open();return (await Store(db).ReadAsync(id,new((a??actor).Subject,SystemRole.Engineering),default))!;}
        async Task<EngineeringCommandResult> Run(EngineeringCommand c,DecisionActor? a=null) {await using var db=open();return await Store(db).ExecuteAsync(id,c,a??actor,default);}
        async Task Check(Guid structure,long revision,DecisionActor a,bool completed=true){await using var db=open();await Store(db).CheckAsync(id,structure,new(Guid.NewGuid(),revision,completed),a,default);}
        async Task Error(string code,Func<Task> f)=>Assert.Equal(code,(await Assert.ThrowsAsync<DocumentException>(f)).Code);
        EngineeringCommand Command(string action,long revision,Guid? target=null,string? subject=null,IReadOnlyList<EngineeringReference>? refs=null,Guid? doc=null)=>new(Guid.NewGuid(),revision,action,target,subject,refs,doc);
        EngineeringReference Reference(EngineeringRow r)=>new(r.Structure.Id,r.Origin!.SubmissionId,r.Origin.EvidenceVersion);
        var before=await Read();var w=before.Workspace!;Assert.Equal(4,w.Packages.Count);Assert.Empty(w.Actas);
        var refs=new[]{Reference(before.Structures.Single(r=>r.Completed)),Reference(before.Structures[3]),Reference(before.Structures[4]),Reference(before.Structures[7])};
        var create=Command("CreatePackage",w.Revision,subject:worker.Subject,refs:refs);
        await Error("EngineeringAccessDenied",()=>Run(create,worker));
        var package=await Run(create);Assert.Equal(package,await Run(create));
        var workerState=await Read(worker);Assert.Equal(4,workerState.Structures.Count(r=>r.Work!.CanModify));
        Assert.False(workerState.Workspace!.CanManagePackages);Assert.Empty(workerState.Workspace.AssignableSubjects);
        await Error("EngineeringAccessDenied",()=>Check(refs[0].StructureId,1,actor));
        await Error("EngineeringAssignmentConflict",()=>Run(create with{RequestKey=Guid.NewGuid(),ExpectedRevision=workerState.Workspace.Revision}));
        var actaCommand=Command("CreateActa",workerState.Workspace.Revision,refs:refs);
        var acta=await Run(actaCommand,worker);Assert.Equal(acta,await Run(actaCommand,worker));
        await Error("EngineeringSourceInvalid",async ()=>await Run(actaCommand with{RequestKey=Guid.NewGuid(),ExpectedRevision=(await Read()).Workspace!.Revision,Structures=[refs[0] with{EvidenceVersion=2}]},worker));
        await Error("EngineeringStructureReserved",async ()=>await Run(actaCommand with{RequestKey=Guid.NewGuid(),ExpectedRevision=(await Read()).Workspace!.Revision},worker));
        await Error("EngineeringStructureReserved",()=>Run(Command("ReassignPackage",0,package.TargetId,actor.Subject)));
        await Error("EngineeringActaRequired",()=>Run(Command("ConfirmActa",0,acta.TargetId),worker));
        await Error("EngineeringAccessDenied",()=>Run(Command("ConfirmActa",0,acta.TargetId)));
        // Disjoint workers can save independently without a global checklist revision.
        var own=before.Structures.First(r=>!r.Completed && r.Origin!.Number==1);
        await Task.WhenAll(Check(refs[1].StructureId,0,worker),Check(own.Structure.Id,0,actor),Check(refs[2].StructureId,0,worker),Check(refs[3].StructureId,0,worker));
        await Error("EngineeringRevisionConflict",()=>Check(refs[1].StructureId,0,worker,false));
        async IAsyncEnumerable<DocumentUpload> Upload(string name="acta.pdf") {using var stream=new MemoryStream(pdf);yield return new("file-0",name,"application/pdf",null,stream);await Task.CompletedTask;}
        async Task UploadActa(Guid a,Guid key,long rev,string hash,DecisionActor who,string name="acta.pdf") {await using var db=open();await Store(db).UploadOpeningAsync(id,a,key,rev,hash,Upload(name),who,default);}
        var current=(await Read(worker)).Workspace!.Actas.Single();var uploadKey=Guid.NewGuid();
        await Error("EngineeringDocumentScopeConflict",()=>UploadActa(acta.TargetId,uploadKey,0,new string('A',64),worker));
        await UploadActa(acta.TargetId,uploadKey,0,current.ScopeHash,worker);
        await UploadActa(acta.TargetId,uploadKey,0,current.ScopeHash,worker);
        current=(await Read(worker)).Workspace!.Actas.Single();Assert.Equal("Ready",current.Status);Assert.Single(current.Documents);
        var firstDoc=current.CurrentDocumentId!.Value;
        await UploadActa(acta.TargetId,Guid.NewGuid(),current.Revision,current.ScopeHash,worker,"revision.pdf");
        current=(await Read(worker)).Workspace!.Actas.Single();Assert.Equal(2,current.Documents.Count);
        await Run(Command("SelectDocument",current.Revision,acta.TargetId,doc:firstDoc),worker);
        var state=await Read();
        var otherRefs=new[]{Reference(own)};
        var second=await Run(Command("CreateActa",state.Workspace!.Revision,refs:otherRefs));
        state=await Read();var secondState=state.Workspace!.Actas.Single(a=>a.Id==second.TargetId);
        await Error("EngineeringActaRequired",()=>Run(Command("SelectDocument",secondState.Revision,second.TargetId,doc:firstDoc)));
        await UploadActa(second.TargetId,Guid.NewGuid(),secondState.Revision,secondState.ScopeHash,actor,"second.pdf");
        state=await Read();secondState=state.Workspace!.Actas.Single(a=>a.Id==second.TargetId);
        // Explicit manifest expansion leaves old PDF immutable and invalidates its coverage.
        var extra=Reference(before.Structures.First(r=>!r.Completed && r.Origin!.Number==1 && r.Structure.Id!=own.Structure.Id));await Run(Command("AddStructures",secondState.Revision,second.TargetId,refs:[extra]));
        state=await Read();secondState=state.Workspace!.Actas.Single(a=>a.Id==second.TargetId);Assert.False(secondState.Documents.Single().MatchesManifest);
        await Error("EngineeringIncomplete",()=>Run(Command("ConfirmActa",secondState.Revision,second.TargetId)));
        await Check(extra.StructureId,0,actor);
        await Error("EngineeringDocumentScopeConflict",()=>Run(Command("ConfirmActa",secondState.Revision,second.TargetId)));
        await UploadActa(second.TargetId,Guid.NewGuid(),secondState.Revision,secondState.ScopeHash,actor,"second-expanded.pdf");
        current=(await Read(worker)).Workspace!.Actas.Single(a=>a.Id==acta.TargetId);
        var confirm=Command("ConfirmActa",current.Revision,acta.TargetId);
        var confirmed=await Task.WhenAll(Run(confirm,worker),Run(confirm,worker));Assert.Equal(confirmed[0],confirmed[1]);
        state=await Read();current=state.Workspace!.Actas.Single(a=>a.Id==acta.TargetId);
        Assert.Equal("Confirmed",current.Status);Assert.False(current.CanModify);Assert.Equal(worker.Subject,current.ConfirmedBySubject);
        Assert.Equal("Completed",state.Workspace.Packages.Single(p=>p.Id==package.TargetId).Status);Assert.Equal("InProgress",state.TaskStatus);
        Assert.NotEqual("Confirmed",state.Workspace.Actas.Single(a=>a.Id==second.TargetId).Status);
        await Error("EngineeringLocked",()=>Run(Command("AddStructures",current.Revision,acta.TargetId,refs:[Reference(before.Structures[4])]),worker));
        await Error("EngineeringAccessDenied",()=>Check(refs[0].StructureId,1,worker,false));
        await Error("EngineeringLocked",()=>UploadActa(acta.TargetId,Guid.NewGuid(),current.Revision,current.ScopeHash,worker));
        await using var verify=open();
        var download=await Store(verify).DownloadOpeningAsync(id,acta.TargetId,firstDoc,new(worker.Subject,SystemRole.Engineering),default);using var output=new MemoryStream();await download.Content.CopyToAsync(output);Assert.Equal(pdf,output.ToArray());await download.Content.DisposeAsync();
        await Error("DocumentNotFound",()=>Store(verify).DownloadOpeningAsync(id,second.TargetId,firstDoc,new(actor.Subject,SystemRole.Engineering),default));
        Assert.Empty(await verify.OpeningRecordRounds.Where(r=>r.ProjectId==id).ToArrayAsync());
        Assert.Single(await verify.ProjectTasks.Where(t=>t.ProjectId==id && t.Type==ProjectTaskType.PresentOpeningRecord).ToArrayAsync());
        Assert.NotEmpty(await verify.Set<EngineeringWorkspaceEvent>().Where(e=>e.ProjectId==id).ToArrayAsync());
        await using(var tx=await verify.Database.BeginTransactionAsync()) {
            var violation=await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"EngineeringEvidenceChecks\" SET \"Completed\"=false WHERE \"ProjectId\"={id} AND \"StructureId\"={refs[0].StructureId}"));Assert.Equal("23514",violation.SqlState);await tx.RollbackAsync();
        }
        secondState=(await Read()).Workspace!.Actas.Single(a=>a.Id==second.TargetId);await Run(Command("ConfirmActa",secondState.Revision,second.TargetId));
        Assert.Equal(2,(await Read()).Workspace!.Actas.Count(a=>a.Status=="Confirmed"));
        // A third package remains reassignable until it participates in an Acta, preserving both assignments.
        state=await Read();var third=await Run(Command("CreatePackage",state.Workspace!.Revision,subject:worker.Subject,refs:[Reference(before.Structures[5])]));
        await Run(Command("ReassignPackage",0,third.TargetId,actor.Subject));
        state=await Read();Assert.Equal(2,state.Workspace!.Packages.Single(p=>p.Id==third.TargetId).History.Count);
        await Error("EngineeringAccessDenied",()=>Check(before.Structures[5].Structure.Id,0,worker));
        await Check(before.Structures[5].Structure.Id,0,actor);
        Assert.Equal(before.Structures.Select(r=>r.Structure),state.Structures.Select(r=>r.Structure));
        // Two different request keys cannot reserve the same physical structure concurrently.
        state=await Read();var race=Command("CreateActa",state.Workspace!.Revision,refs:[Reference(before.Structures[6])]);
        async Task<int> Reserve(EngineeringCommand c){try{await Run(c);return 200;}catch(DocumentException e){Assert.Equal(409,e.Status);return 409;}}
        var races=await Task.WhenAll(Reserve(race),Reserve(race with{RequestKey=Guid.NewGuid()}));Assert.Single(races,s=>s==200);Assert.Single(races,s=>s==409);
        state=await Read();var raced=state.Workspace!.Actas.Single(a=>a.Structures.Any(m=>m.StructureId==before.Structures[6].Structure.Id));
        // Competing document writes are serialized by the Acta revision; neither overwrites bytes.
        async Task<int> ConcurrentUpload(string name){try{await UploadActa(raced.Id,Guid.NewGuid(),raced.Revision,raced.ScopeHash,actor,name);return 200;}catch(DocumentException e){Assert.Equal("EngineeringRevisionConflict",e.Code);return 409;}}
        var uploads=await Task.WhenAll(ConcurrentUpload("first-race.pdf"),ConcurrentUpload("second-race.pdf"));Assert.Single(uploads,s=>s==200);Assert.Single(uploads,s=>s==409);
        Assert.Single((await Read()).Workspace!.Actas.Single(a=>a.Id==raced.Id).Documents);
        await Error("EngineeringSourceInvalid",()=>Run(Command("CreateActa",state.Workspace.Revision,refs:[new(Guid.NewGuid(),Guid.NewGuid(),1)])));
        await IncrementalCommercialOpeningPostgresChecks.VerifyAsync(open,id,storage,timing,pdf);

    }
}
