using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Application.Features.QuotationViews;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Persistence.Stores;

// Reads normalized snapshots only. No workbook, file content, or tracked entities are loaded.
public sealed class EfQuotationReadStore(SnGControlDbContext context) : IQuotationReadStore
{
    public async Task<QuotationReadAccess?> GetAccessAsync(Guid projectId, CancellationToken token)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, token);
        if (project is null) return null;
        return new(project, await context.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == projectId).ToArrayAsync(token));
    }
    public async Task<ConfirmedBudgetQuotationView?> GetBudgetAsync(Guid projectId, CancellationToken token)
    {
        var snapshot = await (from v in context.BudgetQuotationVersions.AsNoTracking()
                              join c in context.CommercialCatalogs.AsNoTracking() on v.Id equals c.BudgetQuotationVersionId
                              where v.ProjectId == projectId
                              orderby v.VersionNumber descending
                              select new { Version = v, CatalogId = c.Id }).FirstOrDefaultAsync(token);
        if (snapshot is null) return null;
        var rows = await (from i in context.CommercialCatalogItems.AsNoTracking()
                          join e in context.CommercialCatalogItemExtractions.AsNoTracking() on i.Id equals e.CatalogItemId into extractions
                          from e in extractions.DefaultIfEmpty()
                          where i.CommercialCatalogId == snapshot.CatalogId
                          orderby i.Section, i.SourceRow
                          select new { Item = i, Extraction = e }).ToArrayAsync(token);
        var items = rows.Select(row =>
        {
            var i = row.Item; var e = row.Extraction;
            var confirmed = CommercialTechnicalDifferences.Values(i);
            var extracted = e is null ? null : new CatalogItemValues(e.ItemNumber, e.Location, e.System, e.Glass,
                e.AluminumFinish, e.LockType, e.Observations, e.Width, e.Height, e.SquareMeters, e.Quantity);
            return new BudgetQuotationReadItem(i.Id, i.Section, i.SourceRow, confirmed, extracted, e?.ImageStatus,
                e?.ImageStatus == CatalogItemImageStatus.Stored && e.ImageStorageKey is not null,
                extracted is null ? [] : CommercialTechnicalDifferences.Compare(extracted, confirmed));
        }).ToArray();
        var version = snapshot.Version;
        return new(projectId, version.Id, version.VersionNumber, version.ConfirmedAt,
            new(version.OriginalFileName, version.DocumentClientName, version.DocumentProjectName, version.DocumentLocation,
                version.ClientLine, version.ProductionLine, version.PreparedBy, version.DocumentSgCode), items);
    }
    public Task<string?> GetBudgetOriginalKeyAsync(Guid projectId, Guid versionId, CancellationToken token) =>
        context.BudgetQuotationVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.Id == versionId)
            .Select(v => (string?)v.OriginalFileStorageKey).SingleOrDefaultAsync(token);
    public async Task<ConfirmedCommercialQuotationView?> GetCommercialAsync(Guid projectId, CancellationToken token)
    {
        var quotation = await context.CurrentCommercial().SingleOrDefaultAsync(q => q.ProjectId == projectId, token);
        if (quotation is null) return null;
        var rows = await (from i in context.CommercialQuotationItems.AsNoTracking()
                          join b in context.CommercialCatalogItems.AsNoTracking() on i.CommercialCatalogItemId equals b.Id
                          join e in context.CommercialCatalogItemExtractions.AsNoTracking() on b.Id equals e.CatalogItemId into extractions
                          from e in extractions.DefaultIfEmpty()
                          where i.CommercialQuotationId == quotation.Id && b.CommercialCatalogId == quotation.CommercialCatalogId
                          orderby i.Section, i.SourceRow
                          select new { Item = i, Budget = b, Extraction = e }).ToArrayAsync(token);
        var items = rows.Select(row =>
        {
            var i = row.Item;
            var values = new CatalogItemValues(i.ItemNumber, i.Location, i.System, i.Glass, i.AluminumFinish,
                i.LockType, i.Observations, i.Width, i.Height, i.SquareMeters, i.Quantity);
            return new CommercialQuotationReadItem(i.Section, i.SourceRow, i.CommercialCatalogItemId, values,
                i.UnitPrice, i.TotalPrice, CommercialTechnicalDifferences.Compare(CommercialTechnicalDifferences.Values(row.Budget), values),
                row.Extraction?.ImageStatus, row.Extraction?.ImageStatus == CatalogItemImageStatus.Stored && row.Extraction.ImageStorageKey is not null);
        }).ToArray();
        var round = await context.BudgetQuotationVersions.Where(v => v.Id == quotation.BudgetQuotationVersionId).Select(v => v.VersionNumber).SingleAsync(token);
        return new(projectId, quotation.Id, quotation.ConfirmedAt, quotation.ProjectTotal, items, round);
    }
    public Task<QuotationImageFile?> GetBudgetImageAsync(Guid projectId, Guid itemId, CancellationToken token) =>
        (from v in context.BudgetQuotationVersions.AsNoTracking()
         join c in context.CommercialCatalogs.AsNoTracking() on v.Id equals c.BudgetQuotationVersionId
         join i in context.CommercialCatalogItems.AsNoTracking() on c.Id equals i.CommercialCatalogId
         join e in context.CommercialCatalogItemExtractions.AsNoTracking() on i.Id equals e.CatalogItemId
         where v.ProjectId == projectId && i.Id == itemId
             && e.ImageStatus == CatalogItemImageStatus.Stored && e.ImageStorageKey != null && e.ImageContentType != null
         select new QuotationImageFile(e.ImageStorageKey!, e.ImageContentType!)).SingleOrDefaultAsync(token);
}
