using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Application.Access;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.QuotationViews;

public sealed class QuotationReadService(IQuotationReadStore store, ICurrentUser current,
    IActiveRoleContext active, IStreamingFileStorage storage, IBudgetQuotationEconomicsReader? economics = null)
{
    private async Task<QuotationReadStatus> AuthorizeAsync(Guid id, bool commercial, CancellationToken token)
    {
        var state = await store.GetAccessAsync(id, token);
        if (state is null) return QuotationReadStatus.NotFound;
        var allowed = new ProjectReadScope(current.Subject, active.Role).CanRead(state.Project, state.Tasks)
            && !(commercial && active.Role == SystemRole.Budgets);
        return allowed ? QuotationReadStatus.Success : QuotationReadStatus.Forbidden;
    }

    public async Task<QuotationReadResult<ConfirmedBudgetQuotationView>> BudgetAsync(Guid id, CancellationToken token)
    {
        var access = await AuthorizeAsync(id, false, token);
        if (access != QuotationReadStatus.Success) return new(access);
        var view = await store.GetBudgetAsync(id, token);
        if (view is null) return new(QuotationReadStatus.NotFound);
        var amounts = BudgetQuotationEconomics.Unavailable("OriginalUnavailable");
        if (economics is not null)
        {
            var key = await store.GetBudgetOriginalKeyAsync(id, view.QuotationVersionId, token);
            if (key is not null)
            {
                try
                {
                    await using var original = await storage.OpenReadAsync(key, token);
                    amounts = await economics.ReadAsync(original, view.Items.Select(i => new BudgetEconomicRow(i.Section, i.SourceRow)).ToArray(), token);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
        return new(QuotationReadStatus.Success, view with { Economics = amounts });
    }
    public async Task<QuotationReadResult<ConfirmedCommercialQuotationView>> CommercialAsync(Guid id, CancellationToken token)
    {
        var access = await AuthorizeAsync(id, true, token);
        if (access != QuotationReadStatus.Success) return new(access);
        var view = await store.GetCommercialAsync(id, token);
        return new(view is null ? QuotationReadStatus.NotFound : QuotationReadStatus.Success, view);
    }
    public async Task<QuotationReadResult<QuotationImageContent>> ImageAsync(Guid id, Guid itemId, CancellationToken token)
    {
        var access = await AuthorizeAsync(id, false, token);
        if (access != QuotationReadStatus.Success) return new(access);
        var file = await store.GetBudgetImageAsync(id, itemId, token);
        // Only inert raster formats produced by the existing XLSX parser may be rendered inline.
        if (file is null || file.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/tiff"))
            return new(QuotationReadStatus.NotFound);
        try { return new(QuotationReadStatus.Success, new(await storage.OpenReadAsync(file.StorageKey, token), file.ContentType)); }
        catch (FileNotFoundException) { return new(QuotationReadStatus.NotFound); }
        catch (DirectoryNotFoundException) { return new(QuotationReadStatus.NotFound); }
    }
}
