using SnG.Control.Application.Access;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using SnG.Control.Application.Features.Budgets.Status;

namespace SnG.Control.Infrastructure.Persistence.Stores;

// No IFileStorage/parser/generator dependency. No extraction values or image payloads are loaded.
public sealed class EfBudgetQuotationStatusStore(SnGControlDbContext context) : IBudgetQuotationStatusStore
{
    public async Task<BudgetQuotationStatusResponse?> GetAsync(Guid projectId, ProjectReadScope scope, CancellationToken token)
    {
        var project = await context.Projects.AsNoTracking().Where(scope.Filter(context.ProjectTasks)).SingleOrDefaultAsync(p => p.Id == projectId, token);
        if (project is null) return null;
        var tasks = await context.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == projectId).ToArrayAsync(token);
        var version = await context.BudgetQuotationVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId).OrderByDescending(v => v.VersionNumber)
            .Select(v => new { v.Id, v.VersionNumber, v.ConfirmedAt, v.DocumentSgCode, HasOperationalFile = v.ConfirmedFileStorageKey != null })
            .FirstOrDefaultAsync(token);
        if (version is null)
            return BudgetQuotationStatusResponse.From(project, tasks, new(null, false, false, 0, 0, 0, false, null), null, 0);

        var catalog = await context.CommercialCatalogs.AsNoTracking().Where(c => c.BudgetQuotationVersionId == version.Id)
            .Select(c => new
            {
                ItemCount = context.CommercialCatalogItems.Count(i => i.CommercialCatalogId == c.Id),
                TotalQuantity = context.CommercialCatalogItems.Where(i => i.CommercialCatalogId == c.Id).Sum(i => (long?)i.Quantity) ?? 0,
                ExtractionCount = context.CommercialCatalogItemExtractions.Count(e =>
                    context.CommercialCatalogItems.Any(i => i.CommercialCatalogId == c.Id && i.Id == e.CatalogItemId)),
                DistinctExtractionItems = context.CommercialCatalogItemExtractions.Where(e =>
                    context.CommercialCatalogItems.Any(i => i.CommercialCatalogId == c.Id && i.Id == e.CatalogItemId)).Select(e => e.CatalogItemId).Distinct().Count(),
                AllItemsCovered = !context.CommercialCatalogItems.Any(i => i.CommercialCatalogId == c.Id
                    && !context.CommercialCatalogItemExtractions.Any(e => e.CatalogItemId == i.Id))
            }).SingleOrDefaultAsync(token);
        var facts = new CommercialHandoffReadiness(version.VersionNumber, version.HasOperationalFile, catalog is not null,
            catalog?.ItemCount ?? 0, catalog?.ExtractionCount ?? 0, catalog?.DistinctExtractionItems ?? 0,
            catalog?.AllItemsCovered ?? false, version.DocumentSgCode);
        return BudgetQuotationStatusResponse.From(project, tasks, facts, version.ConfirmedAt, catalog?.TotalQuantity ?? 0);
    }
}