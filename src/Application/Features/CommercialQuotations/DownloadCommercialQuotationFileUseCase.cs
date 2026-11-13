using SnG.Control.Application.Access;
using SnG.Control.Application.Files;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.CommercialQuotations;

public sealed class DownloadCommercialQuotationFileUseCase(ICommercialQuotationStore store, IFileStorage storage,
    IActiveRoleContext active, ICurrentUser user)
{
    public async Task<CommercialQuotationDownloadResult> ExecuteAsync(Guid projectId, CancellationToken token)
    {
        var state = await store.GetFileAsync(projectId, new ProjectReadScope(user.Subject, active.Role), token);
        if (state is null) return new(CommercialQuotationStatus.ProjectNotFound);
        if (active.Role != SystemRole.CommercialHead
            && !(active.Role == SystemRole.CommercialAdvisor && string.Equals(user.Subject, state.Project.CreatedByGoogleSubject, StringComparison.Ordinal))) return new(CommercialQuotationStatus.Forbidden);
        if (state.Quotation is null) return new(CommercialQuotationStatus.DocumentUnavailable);
        return new(CommercialQuotationStatus.Success, await storage.ReadAsync(state.Quotation.FileStorageKey, token), QuotationDownloadName.Sanitize(state.Quotation.OriginalFileName));
    }
}
