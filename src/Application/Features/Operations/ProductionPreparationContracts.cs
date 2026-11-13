using SnG.Control.Application.Identity;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Application.Features.Operations;
public sealed record PreparationSummary(Guid Id, Guid ProjectId, Guid FilingId, Guid ReleaseVersionId, string ManifestSha256,
    DateTime CreatedAt, string State, long Revision, string? ExtractorVersion, string? ReviewedBySubject);
public sealed record ReleaseSummary(Guid VersionId, Guid ProjectId, Guid FilingId, DateTime ConfirmedAt, string ManifestSha256, Guid? PreparationId);
public sealed record PreparationMember(Guid StructureId, string Op);
public sealed record PreparationDocument(Guid DocumentId, string Sha256, string Category);
public sealed record PreparationDetail(PreparationSummary Preparation, IReadOnlyList<PreparationMember> Members, IReadOnlyList<PreparationDocument> Documents);
public interface IProductionPreparationStore
{
    Task<IReadOnlyList<ReleaseSummary>> ReleasesAsync(int skip, CancellationToken token);
    Task<IReadOnlyList<PreparationSummary>> ListAsync(int skip, CancellationToken token);
    Task<PreparationDetail?> ReadAsync(Guid id, CancellationToken token);
    Task<PreparationDetail> CreateAsync(Guid releaseVersionId, string actor, CancellationToken token);
}
public sealed class ProductionPreparationService(IProductionPreparationStore store, ICurrentUser current, IActiveRoleContext active, IOperationsHeadIdentity operations)
{
    public void RequireRead()
    {
        if (active.Role != SystemRole.OperationsHead || string.IsNullOrWhiteSpace(current.Subject) ||
            !string.Equals(current.Subject, operations.Subject, StringComparison.Ordinal)) throw new DocumentException(403, "OperationsAccessDenied");
    }
    public void RequireWrite() => RequireRead(); // Separate guard; Production/Management never inherit this capability.
    private void Page(int skip) { RequireRead(); if (skip < 0) throw new DocumentException(400, "InvalidOperationsPage"); }
    public Task<IReadOnlyList<ReleaseSummary>> ReleasesAsync(int skip, CancellationToken token) { Page(skip); return store.ReleasesAsync(skip, token); }
    public Task<IReadOnlyList<PreparationSummary>> ListAsync(int skip, CancellationToken token) { Page(skip); return store.ListAsync(skip, token); }
    public Task<PreparationDetail?> ReadAsync(Guid id, CancellationToken token) { RequireRead(); return store.ReadAsync(id, token); }
    public Task<PreparationDetail> CreateAsync(Guid release, CancellationToken token) { RequireWrite(); return store.CreateAsync(release, current.Subject, token); }
}
