using System.Data;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Management;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed partial class EfManagementReadStore
{
    internal IQueryable<QuotationSummary> QuotationSummaryQuery() =>
        context.CurrentCommercial().GroupBy(q => 1)
            .Select(g => new QuotationSummary(g.LongCount(), g.Sum(q => q.ProjectTotal)));

    internal IQueryable<QuotationItemSummary> QuotationItemSummaryQuery() =>
        context.CurrentCommercialItems().GroupBy(i => 1)
            .Select(g => new QuotationItemSummary(g.LongCount(), g.Sum(i => (long)i.Quantity)));

    internal IQueryable<QuotationItemRank> QuotationItemsQuery(bool byUnitPrice) =>
        (from item in context.CurrentCommercialItems()
         join quotation in context.CurrentCommercial() on item.CommercialQuotationId equals quotation.Id
         join project in context.Projects.AsNoTracking() on quotation.ProjectId equals project.Id
         join client in context.Clients.AsNoTracking() on project.ClientId equals client.Id
         orderby (byUnitPrice ? item.UnitPrice : item.TotalPrice) descending,
             EF.Functions.Collate(project.Name, "C"), EF.Functions.Collate(item.ItemNumber, "C"),
             item.SourceRow, project.Id, item.Section
         select new QuotationItemRank(project.Id, project.Name, project.SgCode, client.Name,
             item.Section, item.SourceRow, item.ItemNumber, item.Location, item.System, item.Glass,
             item.AluminumFinish, item.Quantity, item.UnitPrice, item.TotalPrice))
        .Take(GetManagementQuotationPortfolioUseCase.TopLimit);

    internal IQueryable<QuotationProjectRank> QuotationProjectsQuery() =>
        (from quotation in context.CurrentCommercial()
         join project in context.Projects.AsNoTracking() on quotation.ProjectId equals project.Id
         join client in context.Clients.AsNoTracking() on project.ClientId equals client.Id
         orderby quotation.ProjectTotal descending, EF.Functions.Collate(project.Name, "C"), project.Id
         select new QuotationProjectRank(project.Id, project.Name, project.SgCode, client.Name,
             quotation.ConfirmedAt, quotation.ProjectTotal))
        .Take(GetManagementQuotationPortfolioUseCase.TopLimit);

    public async Task<QuotationPortfolioData> GetQuotationPortfolioAsync(CancellationToken cancellationToken)
    {
        // All 21 aggregate/ranking queries share one snapshot, including concurrent confirmations.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var summary = await QuotationSummaryQuery().SingleOrDefaultAsync(cancellationToken) ?? new(0, 0.00m);
        var items = await QuotationItemSummaryQuery().SingleOrDefaultAsync(cancellationToken) ?? new(0, 0);
        var byValue = await QuotationItemsQuery(false).ToListAsync(cancellationToken);
        var byUnit = await QuotationItemsQuery(true).ToListAsync(cancellationToken);
        var projects = await QuotationProjectsQuery().ToListAsync(cancellationToken);
        var mix = new QuotationMix(
            await ReadMixAsync(MixDimension.System, items.ItemCount, SimpleMix, cancellationToken),
            await ReadMixAsync(MixDimension.Glass, items.ItemCount, SimpleMix, cancellationToken),
            await ReadMixAsync(MixDimension.Finish, items.ItemCount, SimpleMix, cancellationToken),
            await ReadMixAsync(MixDimension.Combination, items.ItemCount,
                row => new QuotationCombinationEntry(row.Value, row.Glass, row.ItemCount, row.TotalQuantity, row.TotalQuotedValue), cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(summary, items, byValue, byUnit, projects, mix);
    }
}
