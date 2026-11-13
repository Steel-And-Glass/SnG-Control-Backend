using System.Data;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed class EfManagementSurfaceStore(SnGControlDbContext db, TimeProvider clock) : IManagementSurfaceStore
{
    internal IQueryable<Project> Projects(SurfaceFilter f)
    {
        var q = db.Projects.AsNoTracking();
        if(f.ProjectId is {} project) q=q.Where(p=>p.Id==project);
        if(f.ClientId is {} client) q=q.Where(p=>p.ClientId==client);
        if(f.City is {} city) q=q.Where(p=>p.City==city);
        if(f.ProductLine is {} line) q=q.Where(p=>p.ProductLine==line);
        // Area and status must match the SAME task. They select a project cohort, not a fraction of its surface.
        if(f.Area!=null || f.Status!=null) q=q.Where(p=>db.ProjectTasks.Any(t=>t.ProjectId==p.Id &&
            (f.Area==null || t.Area==f.Area) && (f.Status==null || t.Status==f.Status)));
        return q;
    }
    internal IQueryable<CommercialQuotationItem> Items(SurfaceFilter f) => db.CurrentCommercialItems()
        .Where(i=>db.CurrentCommercial().Any(q=>q.Id==i.CommercialQuotationId && Projects(f).Any(p=>p.Id==q.ProjectId)));
    public async Task<ManagementSurfacesResponse> ReadAsync(SurfaceFilter f, CancellationToken token)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);
        var projects=Projects(f); var items=Items(f);
        var summary=await items.GroupBy(_=>1).Select(g=>new {
            Sum=g.Where(i=>i.SquareMeters>0).Sum(i=>(decimal?)i.SquareMeters),
            Eligible=g.LongCount(i=>i.SquareMeters>0),Excluded=g.LongCount(i=>i.SquareMeters<=0)
        }).SingleOrDefaultAsync(token);
        var total=await projects.CountAsync(token);
        var allStructures=db.Set<MeasurementStructure>().AsNoTracking().Where(s=>projects.Any(p=>p.Id==s.ProjectId));
        var structures=allStructures.Where(s=>s.RetiredAt==null);
        var reference=await allStructures.GroupBy(_=>1).Select(g=>new {
            Sum=g.Where(s=>s.RetiredAt==null && s.ActualWidth>0 && s.ActualHeight>0).Sum(s=>s.ActualWidth*s.ActualHeight),
            Eligible=g.LongCount(s=>s.RetiredAt==null && s.ActualWidth>0 && s.ActualHeight>0),
            Retired=g.LongCount(s=>s.RetiredAt!=null),
            Invalid=g.LongCount(s=>s.RetiredAt==null && (s.ActualWidth==null || s.ActualHeight==null || s.ActualWidth<=0 || s.ActualHeight<=0))
        }).SingleOrDefaultAsync(token);
        var filedCount=await db.Set<MeasurementSubmissionMember>().AsNoTracking()
            .Where(m=>projects.Any(p=>p.Id==m.ProjectId)).Select(m=>new {m.ProjectId,m.StructureId}).Distinct().LongCountAsync(token);
        var itemGroups=from i in items join q in db.CurrentCommercial() on i.CommercialQuotationId equals q.Id
            group i by q.ProjectId into g select new {ProjectId=g.Key,
                Area=g.Where(i=>i.SquareMeters>0).Sum(i=>(decimal?)i.SquareMeters),Count=g.LongCount(i=>i.SquareMeters>0)};
        var rows=from p in projects join c in db.Clients.AsNoTracking() on p.ClientId equals c.Id
            join summaryRow in itemGroups on p.Id equals summaryRow.ProjectId into summaries
            from itemSummary in summaries.DefaultIfEmpty()
            select new {p.Id,p.ClientId,p.Name,ClientName=c.Name,p.SgCode,p.City,p.ProductLine,
                Area=itemSummary.Area,Count=(long?)itemSummary.Count??0,
                Reference=structures.Where(s=>s.ProjectId==p.Id && s.ActualWidth>0 && s.ActualHeight>0).Sum(s=>s.ActualWidth*s.ActualHeight),
                Measured=structures.LongCount(s=>s.ProjectId==p.Id && s.ActualWidth>0 && s.ActualHeight>0),
                Pending=structures.LongCount(s=>s.ProjectId==p.Id && s.MeasuredAt==null)};
        var sorted=f.Ascending?rows.OrderBy(r=>r.Count==0).ThenBy(r=>r.Area).ThenBy(r=>r.Id)
            :rows.OrderBy(r=>r.Count==0).ThenByDescending(r=>r.Area).ThenBy(r=>r.Id);
        var page=await sorted.Skip(f.Skip).Take(50).ToArrayAsync(token);
        var result=new ManagementSurfacesResponse(clock.GetUtcNow().UtcDateTime,total,f.Skip,50,f.Skip+page.Length<total,
            SurfaceDefinition.Quotation(summary?.Sum,summary?.Eligible??0,summary?.Excluded??0),
            new(reference?.Eligible>0?SurfaceDefinition.Exact(reference.Sum??0):null,"m2",reference?.Eligible??0,(reference?.Retired??0)+(reference?.Invalid??0),
                "MeasurementStructure.ActualWidth/ActualHeight (m), estado actual",SurfaceDefinition.Reference,
                new Dictionary<string,long>{["RetiredStructure"]=reference?.Retired??0,["MissingOrInvalidDimensions"]=reference?.Invalid??0}),
            SurfaceDefinition.Unavailable(filedCount,"MeasurementSubmissionMember frozen evidence","FrozenVersionSelectionNotDefined"),
            page.Select(r=>new SurfaceProject(r.Id,r.ClientId,r.Name,r.ClientName,r.SgCode,r.City,r.ProductLine.ToString(),
                r.Count==0?null:SurfaceDefinition.Exact(r.Area??0),r.Count,r.Measured==0?null:SurfaceDefinition.Exact(r.Reference??0),r.Measured,r.Pending)).ToArray());
        await tx.CommitAsync(token);return result;
    }
}
