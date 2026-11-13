using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Application.Features.CommercialQuotations;

public sealed class ConfirmCommercialQuotationUseCase(PreviewCommercialQuotationUseCase preview, ICommercialQuotationStore store,
    ICommercialHeadIdentity head, ICurrentUser user)
{
    public async Task<CommercialQuotationConfirmationResult> ExecuteAsync(Guid projectId, byte[] bytes, string fileName, CancellationToken token)
    {
        var prepared = await preview.PrepareAsync(projectId, bytes, fileName, true, token);
        if (prepared.Result.Status != CommercialQuotationStatus.Success) return new(prepared.Result.Status, Validation: prepared.Result);
        var state = prepared.State!; var response = prepared.Result.Response!;
        var quotation = new CommercialQuotation(projectId, state.Version!.Id, state.Catalog!.Id, fileName,
            response.ProjectTotal, user.Subject, user.Email, user.Name);
        var items = response.Items.Select(i => new CommercialQuotationItem(quotation.Id, i.CommercialCatalogItemId, i.Section, i.SourceRow,
            i.Values, i.UnitPrice, i.TotalPrice)).ToArray();
        return await store.ConfirmAsync(state, head.Subject, quotation, items, bytes, response, token);
    }
}
