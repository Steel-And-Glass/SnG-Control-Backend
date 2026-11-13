using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Stores;

internal static class QuotationQueries
{
    // Ordering is the official Budget version, never timestamps or task counts.
    public static IQueryable<CommercialQuotation> CurrentCommercial(this SnGControlDbContext db) =>
        from q in db.CommercialQuotations.AsNoTracking()
        join v in db.BudgetQuotationVersions.AsNoTracking() on q.BudgetQuotationVersionId equals v.Id
        where !db.CommercialQuotations.Any(other => other.ProjectId == q.ProjectId &&
            db.BudgetQuotationVersions.Any(newer => newer.Id == other.BudgetQuotationVersionId && newer.VersionNumber > v.VersionNumber))
        select q;
    public static IQueryable<CommercialQuotationItem> CurrentCommercialItems(this SnGControlDbContext db) =>
        db.CommercialQuotationItems.AsNoTracking().Where(i => db.CurrentCommercial().Any(q => q.Id == i.CommercialQuotationId));
}
