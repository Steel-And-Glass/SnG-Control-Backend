using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Entities.Production;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Tests.Domain;
public class ProductionPreparationTests
{
    private static readonly DateTime At = new(2026,11,13,0,0,0,DateTimeKind.Utc);
    private static (ProductionPreparation,OpeningFilingVersion) Source()
    {
        var workspace = new OpeningFilingWorkspace(new OpeningFilingDelivery(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()),"engineer",At);
        var version = new OpeningFilingVersion(workspace,1,null,At,"engineer");
        Assert.Throws<InvalidOperationException>(()=>new OpeningFilingRelease(version));
        version.Confirm(At,"engineer","{}");
        return (new ProductionPreparation(new OpeningFilingRelease(version),At,"operations"),version);
    }
    [Fact] public void OnlyPendingWithoutPublication()
    {
        var (p,_) = Source(); Assert.Equal(PreparationState.PendingExtraction,p.State); Assert.Equal(0,p.Revision);
        Assert.Null(p.ExtractorVersion); Assert.Null(p.ReviewedBySubject);
        Assert.DoesNotContain("Approved",Enum.GetNames<PreparationState>());
        Assert.Equal(10,(int)SystemRole.OperationsHead); Assert.Equal(6,(int)SystemRole.Production);
        Assert.DoesNotContain(typeof(ProductionPreparation).GetMethods(),m=>m.Name is "Approve" or "Publish");
    }
    [Theory][InlineData("")][InlineData(" ")][InlineData(" subject")][InlineData("subject\n")]
    public void InvalidConfigurationRejected(string value)
    {
        var s = new ServiceCollection();s.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());s.AddLogging();s.AddOperationsHead();s.Configure<OperationsHeadOptions>(o=>o.OperationsHeadSubject=value);
        using var sp=s.BuildServiceProvider();Assert.Throws<OptionsValidationException>(()=>sp.GetRequiredService<IOptions<OperationsHeadOptions>>().Value);
    }
    [Fact] public void ValidConfigurationUsesSubject()
    {
        var s=new ServiceCollection();s.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());s.AddLogging();s.AddOperationsHead();s.Configure<OperationsHeadOptions>(o=>o.OperationsHeadSubject="synthetic-operations");
        using var sp=s.BuildServiceProvider();Assert.Equal("synthetic-operations",sp.GetRequiredService<IOperationsHeadIdentity>().Subject);
    }
    [Fact] public void ExtractionRetriesAreBoundedAndFinalResultsAreNotRequeued()
    {
        var job=new DocumentExtractionJob(Guid.NewGuid(),"p2.2.1","synthetic",At);
        for(var attempt=1;attempt<=3;attempt++)
        {
            job.Claim(At.AddMinutes(attempt),90);job.Finish(ExtractionJobState.Failed,"{}","ProcessorFailed",At.AddMinutes(attempt));
            Assert.Equal(attempt<3,job.Retry("synthetic",At.AddMinutes(attempt)));
        }
        Assert.Equal(3,job.Attempts);Assert.Throws<InvalidOperationException>(()=>job.Claim(At.AddHours(1),90));
        var (preparation,_)=Source();preparation.RecordExtraction(PreparationState.ReadyForReview,"p2.2.1");
        Assert.Throws<InvalidOperationException>(()=>preparation.RecordExtraction(PreparationState.PendingExtraction,"p2.2.1"));
        Assert.Throws<ArgumentException>(()=>preparation.RecordExtraction((PreparationState)99,"p2.2.1"));
    }

}
