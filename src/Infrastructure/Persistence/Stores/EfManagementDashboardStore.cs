using System.Data;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Management;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Stores;

// One RepeatableRead snapshot, a constant number of queries, all aggregates grouped by project.
public sealed class EfManagementDashboardStore(SnGControlDbContext db, TimeProvider clock) : IManagementDashboardStore
{
    public async Task<ManagementDashboardSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var projects = await (from p in db.Projects.AsNoTracking()
            join c in db.Clients.AsNoTracking() on p.ClientId equals c.Id
            select new { p.Id, p.ClientId, p.Name, ClientName = c.Name, p.SgCode, p.City, p.ProductLine })
            .ToListAsync(cancellationToken);

        var quotations = await db.CurrentCommercial()
            .Select(q => new { q.ProjectId, q.ProjectTotal })
            .ToListAsync(cancellationToken);

        var quotationItems = await (from i in db.CurrentCommercialItems()
            join q in db.CurrentCommercial() on i.CommercialQuotationId equals q.Id
            group i by q.ProjectId into g
            select new
            {
                ProjectId = g.Key,
                Area = g.Where(i => i.SquareMeters > 0).Sum(i => (decimal?)i.SquareMeters),
                Count = g.LongCount(i => i.SquareMeters > 0)
            }).ToListAsync(cancellationToken);

        var structures = await db.Set<MeasurementStructure>().AsNoTracking()
            .Where(s => s.RetiredAt == null)
            .GroupBy(s => s.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Reference = g.Where(s => s.ActualWidth > 0 && s.ActualHeight > 0).Sum(s => (decimal?)(s.ActualWidth * s.ActualHeight)),
                Measured = g.LongCount(s => s.ActualWidth > 0 && s.ActualHeight > 0),
                Pending = g.LongCount(s => s.MeasuredAt == null)
            }).ToListAsync(cancellationToken);

        var tasks = await (from task in db.ProjectTasks.AsNoTracking()
            join project in db.Projects.AsNoTracking() on task.ProjectId equals project.Id
            join client in db.Clients.AsNoTracking() on project.ClientId equals client.Id
            select new ManagementTaskData(task, project.Name, client.Id, client.Name, project.SgCode, project.City, project.ProductLine))
            .ToListAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        var quotationByProject = quotations.ToDictionary(q => q.ProjectId, q => q.ProjectTotal);
        var itemByProject = quotationItems.ToDictionary(i => i.ProjectId);
        var structureByProject = structures.ToDictionary(s => s.ProjectId);

        var rows = projects.Select(p =>
        {
            var hasQuotation = quotationByProject.TryGetValue(p.Id, out var quotedValue);
            var item = itemByProject.GetValueOrDefault(p.Id);
            var structure = structureByProject.GetValueOrDefault(p.Id);
            return new ManagementDashboardProjectRow(p.Id, p.ClientId, p.Name, p.ClientName, p.SgCode, p.City, p.ProductLine,
                hasQuotation, item is { Count: > 0 } ? item.Area : null, item?.Count ?? 0,
                hasQuotation ? quotedValue : (decimal?)null, structure?.Reference, structure?.Measured ?? 0, structure?.Pending ?? 0);
        }).ToArray();

        return new(clock.GetUtcNow().UtcDateTime, rows, tasks);
    }
}
