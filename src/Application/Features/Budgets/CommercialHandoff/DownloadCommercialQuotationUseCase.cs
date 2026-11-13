using SnG.Control.Application.Access;
using SnG.Control.Application.Files;

namespace SnG.Control.Application.Features.Budgets.CommercialHandoff;

public sealed record CommercialFileResult(CommercialHandoffStatus Status, byte[]? Bytes = null, string? FileName = null);

public sealed class DownloadCommercialQuotationUseCase(IBudgetQuotationCommercialHandoffStore store, IFileStorage storage, ProjectReadAccess access)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public async Task<CommercialFileResult> ExecuteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var file = await store.GetFileAsync(projectId, access.Scope, cancellationToken);
        if (!file.ProjectExists) return new(CommercialHandoffStatus.ProjectNotFound);
        if (file.StorageKey is null || !file.ReviewExists) return new(CommercialHandoffStatus.InvalidState);
        // A missing referenced file is an unexpected server failure; do not regenerate it or expose its path.
        return new(CommercialHandoffStatus.Sent, await storage.ReadAsync(file.StorageKey, cancellationToken), QuotationDownloadName.Sanitize(file.FileName!));
    }
}
