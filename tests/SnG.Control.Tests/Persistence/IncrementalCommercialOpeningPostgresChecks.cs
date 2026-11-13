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
internal static class IncrementalCommercialOpeningPostgresChecks
{
    private sealed class Team:IEngineeringAssigneeIdentity,IRoleResolver {
        public string Subject=>"engineer";public IReadOnlyList<string> Subjects=>["engineer","worker-two"];
        public IReadOnlyList<SystemRole> Resolve(string subject)=>subject=="advisor"?[SystemRole.CommercialAdvisor]:Subjects.Contains(subject)?[SystemRole.Engineering]:[];
    }
    private sealed class GatedStorage(IStreamingFileStorage inner):IStreamingFileStorage {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Stream> OpenReadAsync(string key,CancellationToken token){Entered.TrySetResult();await Release.Task.WaitAsync(token);return await inner.OpenReadAsync(key,token);}
        public Task<byte[]> ReadAsync(string key,CancellationToken token)=>inner.ReadAsync(key,token);
        public Task SaveAsync(string key,ReadOnlyMemory<byte> bytes,CancellationToken token)=>inner.SaveAsync(key,bytes,token);
        public Task DeleteAsync(string key,CancellationToken token)=>inner.DeleteAsync(key,token);
        public Task SaveFromStreamAsync(string key,Stream stream,CancellationToken token)=>inner.SaveFromStreamAsync(key,stream,token);
    }
    public static async Task VerifyAsync(Func<SnGControlDbContext> open,Guid id,IStreamingFileStorage storage,TaskTiming timing,byte[] pdf)
    {
        EfCommercialOpeningStore Store(SnGControlDbContext db)=>new(db,timing,new(db,new ProjectNotificationPlanner(timing)),storage,new Team(),new Team());
        var advisor=new DecisionActor("advisor","advisor@example.test","Advisor");var worker=new DecisionActor("worker-two","worker@example.test","Engineer");
        async Task<CommercialOpeningState> Read(){await using var db=open();return (await Store(db).ReadAsync(id,new(advisor.Subject,SystemRole.CommercialAdvisor),default))!;}
        async Task<CommercialOpeningResult> Run(Guid acta,CommercialOpeningCommand c,bool engineer=false){await using var db=open();return await Store(db).CommandAsync(id,acta,c,engineer?worker:advisor,engineer?SystemRole.Engineering:SystemRole.CommercialAdvisor,default);}
        async Task Error(string code,Func<Task> f)=>Assert.Equal(code,(await Assert.ThrowsAsync<DocumentException>(f)).Code);
        CommercialOpeningSelection Ref(CommercialOpeningRowView row,string? reason=null)=>new(row.Structure.Id,row.Revision,row.EvidenceVersion,reason);
        CommercialOpeningCommand Cmd(string action,CommercialOpeningRoundView r)=>new(Guid.NewGuid(),action,r.Revision,r.Id);
        async Task Upload(Guid acta,CommercialOpeningRoundView r,CommercialOpeningUpload spec,bool engineer=false){
            async IAsyncEnumerable<DocumentUpload> Files(){using var stream=new MemoryStream(pdf);yield return new("file","synthetic.pdf","application/pdf",null,stream);await Task.CompletedTask;}
            await using var db=open();await Store(db).UploadAsync(id,acta,r.Id,spec,Files(),engineer?worker:advisor,engineer?SystemRole.Engineering:SystemRole.CommercialAdvisor,default);
        }
        var state=await Read();Assert.Equal(2,state.Actas.Count);Assert.All(state.Actas,a=>Assert.Equal(1,Assert.Single(a.Rounds).Number));
        var a=state.Actas.Single(a=>a.Rounds[0].Structures.Count==4);var b=state.Actas.Single(other=>other.Id!=a.Id);
        var r=a.Rounds[0];var rb=b.Rounds[0];
        Assert.Null(r.ElapsedSeconds);Assert.True(r.CanPresent);Assert.False(r.CanRespond);
        var early=Cmd("RequestChanges",r) with{Structures=[Ref(r.Structures[0],"Early")]};await Error("OpeningRoundStateConflict",()=>Run(a.Id,early));
        var present=Cmd("Present",r);await Run(a.Id,present);await Run(a.Id,present);await Run(b.Id,Cmd("Present",rb));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();Assert.NotNull(r.ElapsedSeconds);Assert.NotNull(r.PresentedAt);
        var first=r.Structures[0];var second=r.Structures[1];var third=r.Structures[2];
        // Same signed bytes safely cover two disjoint partial acceptances, never the omitted third.
        var upload=new CommercialOpeningUpload(Guid.NewGuid(),r.Revision,"SIGNED",null,[Ref(first),Ref(third)]);
        await Upload(a.Id,r,upload);await Upload(a.Id,r,upload);
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);var signed=Assert.Single(a.Documents,d=>d.Kind=="SIGNED");
        var accept=Cmd("Accept",r) with{Structures=[Ref(first)],DocumentId=signed.Document.Id,Attest=true};
        // Hold acceptance after it acquired the project lock; a simultaneous change cannot overwrite it.
        var gated=new GatedStorage(storage);CommercialOpeningResult accepted;
        await using(var racingDb=open()){
            var accepting=new EfCommercialOpeningStore(racingDb,timing,new(racingDb,new ProjectNotificationPlanner(timing)),gated,new Team(),new Team()).CommandAsync(id,a.Id,accept,advisor,SystemRole.CommercialAdvisor,default);
            await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var conflict=Error("OpeningRevisionConflict",()=>Run(a.Id,Cmd("RequestChanges",r) with{Structures=[Ref(first,"Concurrent change")]}));
            gated.Release.TrySetResult();accepted=await accepting;await conflict;
        }
        Assert.Equal(accepted,await Run(a.Id,accept));
        await Error("OpeningSignedActaRequired",()=>Run(b.Id,Cmd("Accept",rb) with{Structures=[Ref(rb.Structures[0])],DocumentId=signed.Document.Id,Attest=true}));
        await using(var denied=open())await Error("OpeningAccessDenied",()=>Store(denied).CommandAsync(id,a.Id,accept,advisor with{Subject="unassigned"},SystemRole.CommercialAdvisor,default));
        await using(var denied=open())await Error("OpeningAccessDenied",()=>Store(denied).CommandAsync(id,a.Id,accept,advisor,SystemRole.Management,default));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();Assert.Equal("Confirmed",r.Structures[0].Approval);Assert.Equal("AwaitingApproval",r.Structures[1].Approval);Assert.Equal("AwaitingApproval",r.Structures[2].Approval);Assert.Null(r.ResolvedAt);Assert.NotNull(r.FirstResponseAt);
        var request=Cmd("RequestChanges",r) with{Structures=[Ref(second,"Adjust second structure")],Observations="Synthetic client observation"};
        var reviewResult=await Run(a.Id,request);Assert.Equal(reviewResult,await Run(a.Id,request));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();var review=Assert.Single(a.Reviews);
        Assert.Equal("UnderEngineeringReview",r.Structures[1].Approval);Assert.Equal("AwaitingApproval",r.Structures[2].Approval);
        await Error("OpeningEvidenceConflict",()=>Run(a.Id,Cmd("Accept",r) with{Structures=[Ref(r.Structures[1])],DocumentId=signed.Document.Id,Attest=true}));
        // The same PDF remains reusable while another structure is under review.
        await Run(a.Id,Cmd("Accept",r) with{Structures=[Ref(third)],DocumentId=signed.Document.Id,Attest=true});
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();Assert.Equal(2,a.Acceptances.Count);Assert.Equal(2,a.Acceptances.Select(x=>x.TaskId).Distinct().Count());Assert.All(a.Acceptances,x=>Assert.Equal(signed.Document.Id,x.SignedDocumentId));
        var check=Cmd("ReviewCheck",r) with{ReviewId=review.Id,Structures=[new(second.Structure.Id,review.Structures.Single().Revision,second.EvidenceVersion)],Completed=true};
        await Run(a.Id,check,true);
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);review=a.Reviews.Single();Assert.True(review.Structures.Single().Completed);
        // Another request preserves reasons, resets only that check and invalidates the declaration.
        await Run(a.Id,Cmd("RequestChanges",r) with{Structures=[Ref(r.Structures[1],"Additional explicit reason")]});
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();review=a.Reviews.Single();Assert.False(review.Structures.Single().Completed);Assert.False(review.Declared);Assert.Equal(2,a.History.Count(e=>e.Action=="RequestChanges"));
        await Run(a.Id,Cmd("DeclareChanged",r) with{ReviewId=review.Id,ExpectedRevision=review.Revision,Structures=[Ref(r.Structures[1])]},true);
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();review=a.Reviews.Single();
        await Run(a.Id,Cmd("ReviewCheck",r) with{ReviewId=review.Id,Structures=[new(second.Structure.Id,review.Structures.Single().Revision,second.EvidenceVersion)],Completed=true},true);
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();review=a.Reviews.Single();
        await Upload(a.Id,r,new(Guid.NewGuid(),review.Revision,"REVISED",review.Id,r.Structures.Select(x=>Ref(x)).ToArray()),true);
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single();review=a.Reviews.Single();
        var close=Cmd("CloseReview",r) with{ReviewId=review.Id,ExpectedRevision=review.Revision,DocumentId=review.DocumentId};
        var next=await Run(a.Id,close,true);Assert.Equal(next,await Run(a.Id,close,true));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Single(x=>x.Id==next.Id);Assert.Equal(2,r.Number);Assert.Null(r.PresentedAt);Assert.Null(r.ElapsedSeconds);
        Assert.Equal("Confirmed",r.Structures[0].Approval);Assert.Equal("AwaitingApproval",r.Structures[1].Approval);Assert.Equal(2,r.Structures[1].EvidenceVersion);Assert.Equal("Confirmed",r.Structures[2].Approval);Assert.Equal(1,r.Structures[0].EvidenceVersion);Assert.Equal("AwaitingApproval",r.Structures[3].Approval);
        Assert.Equal("Superseded",a.Rounds[0].Status);Assert.Equal(1,(await Read()).Actas.Single(x=>x.Id==b.Id).Rounds.Single().Number);
        await Run(a.Id,Cmd("Present",r));
        await Error("OpeningSignedActaRequired",()=>Run(a.Id,Cmd("Accept",r) with{Structures=[Ref(r.Structures[1])],DocumentId=signed.Document.Id,Attest=true}));
        // Two disjoint acceptances reuse one PDF concurrently and only the last resolves this round.
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);r=a.Rounds.Last();
        await Upload(a.Id,r,new(Guid.NewGuid(),r.Revision,"SIGNED",null,[Ref(r.Structures[1]),Ref(r.Structures[3])]));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);var newSigned=a.Documents.Single(x=>x.Kind=="SIGNED"&&x.RoundId==r.Id);
        var acceptOne=Cmd("Accept",r) with{Structures=[Ref(r.Structures[1])],DocumentId=newSigned.Document.Id,Attest=true};
        var acceptTwo=Cmd("Accept",r) with{Structures=[Ref(r.Structures[3])],DocumentId=newSigned.Document.Id,Attest=true};
        await Task.WhenAll(Run(a.Id,acceptOne),Run(a.Id,acceptTwo));
        a=(await Read()).Actas.Single(x=>x.Id==a.Id);Assert.Equal("Resolved",a.Rounds.Last().Status);Assert.NotNull(a.Rounds.Last().ResolutionSeconds);Assert.Equal(4,a.Acceptances.Count);
        // Distinct structures may respond concurrently. A repeated version of the same one conflicts.
        b=(await Read()).Actas.Single(x=>x.Id==b.Id);rb=b.Rounds.Single();var one=Cmd("RequestChanges",rb) with{Structures=[Ref(rb.Structures[0],"One")]};var two=Cmd("RequestChanges",rb) with{Structures=[Ref(rb.Structures[1],"Two")]};
        await Task.WhenAll(Run(b.Id,one),Run(b.Id,two));
        await Error("OpeningRevisionConflict",()=>Run(b.Id,one with{RequestKey=Guid.NewGuid()}));
        await Error("OpeningRequestConflict",()=>Run(b.Id,one with{Observations="Changed retry"}));
        await using var verify=open();
        var filing=await verify.ProjectTasks.Where(t=>t.ProjectId==id&&t.Type==ProjectTaskType.FileOpeningRecord).ToArrayAsync();Assert.Equal(4,filing.Length);Assert.All(filing,t=>{Assert.Equal(ProjectTaskStatus.Pending,t.Status);Assert.Null(t.StartedAt);Assert.Null(t.DeadlineAt);});
        Assert.Equal(4,await verify.Set<CommercialOpeningAcceptanceEvidence>().CountAsync(x=>x.ProjectId==id));
        await using(var tx=await verify.Database.BeginTransactionAsync()){
            var error=await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialOpeningRows\" SET \"Approval\"='AwaitingApproval',\"AcceptanceId\"=null WHERE \"RoundId\"={a.Rounds[0].Id}"));Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        await using(var tx=await verify.Database.BeginTransactionAsync()){
            var error=await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommercialOpeningDocumentMembers\" SET \"EvidenceVersion\"=99 WHERE \"DocumentId\"={signed.Document.Id}"));Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        Assert.Empty(await verify.OpeningRecordRounds.Where(x=>x.ProjectId==id).ToArrayAsync());
    }
}
