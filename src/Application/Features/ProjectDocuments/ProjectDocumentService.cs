using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Application.Features.ProjectDocuments;

public interface IProjectDocumentArchive
{
    Task<Stream> CreateAsync(IReadOnlyList<ProjectDocument> documents, CancellationToken token);
}

public sealed class ProjectDocumentService(IProjectDocumentStore store, ProjectDocumentAccess access,
    IStreamingFileStorage storage, IProjectDocumentArchive archive)
{
    public async Task RequireUploadAccessAsync(Guid id, CancellationToken token)
    {
        var state = await store.LoadAsync(id, token) ?? throw new DocumentException(404, "ProjectNotFound");
        access.RequireModify(state.Project);
    }
    public Task<DocumentBatchResponse> UploadAsync(Guid id, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token) => store.UploadAsync(id, uploads, token);
    public Task RemoveAsync(Guid id, Guid documentId, CancellationToken token) => store.RemoveAsync(id, documentId, token);
    private async Task<ProjectDocumentState> ReadAsync(Guid id, CancellationToken token)
    {
        var state = await store.LoadAsync(id, token) ?? throw new DocumentException(404, "ProjectNotFound");
        if (!access.CanRead(state)) throw new DocumentException(403, "DocumentAccessDenied");
        return state;
    }
    public async Task<DocumentListResponse> ListAsync(Guid id, CancellationToken token)
    {
        var state = await ReadAsync(id, token);
        var initial = state.HandoffItems.Select(i => i.ProjectDocumentId).ToHashSet();
        var canModify = access.CanModify(state.Project);
        var sent = state.PrepareQuotation is not null || state.Handoff is not null;
        return new(state.Documents.Where(d => d.RemovedAt is null && !d.IsPaymentReceipt && !d.IsMeasurementDocument && !d.IsEngineeringDocument).Select(d => DocumentResponse.From(d, initial)).ToArray(),
            state.Handoff is { } h ? new(h.Id, h.SentAt, new(h.SentByEmail, h.SentByName), initial.Order().ToArray()) : null,
            canModify, canModify && !sent, access.CanSendToBudgets(state.Project) && !sent && state.Documents.Any(d => d.IsCurrent && !d.IsPaymentReceipt && !d.IsMeasurementDocument && !d.IsEngineeringDocument));
    }
    public async Task<DocumentDownload> DownloadAsync(Guid id, Guid documentId, CancellationToken token)
    {
        var state = await ReadAsync(id, token);
        var document = state.Documents.SingleOrDefault(d => d.Id == documentId && d.RemovedAt is null && !d.IsEngineeringDocument)
            ?? throw new DocumentException(404, "DocumentNotFound");
        try { return new(await storage.OpenReadAsync(document.StorageKey, token), document.OriginalFileName); }
        catch (FileNotFoundException) { throw new DocumentException(500, "DocumentUnavailable"); }
        catch (DirectoryNotFoundException) { throw new DocumentException(500, "DocumentUnavailable"); }
    }
    public async Task<DocumentDownload> ZipAsync(Guid id, bool initial, CancellationToken token)
    {
        var state = await ReadAsync(id, token);
        if (initial && state.Handoff is null) throw new DocumentException(409, "InitialHandoffUnavailable");
        var ids = state.HandoffItems.Select(i => i.ProjectDocumentId).ToHashSet();
        var documents = state.Documents.Where(d => !d.IsPaymentReceipt && !d.IsMeasurementDocument && !d.IsEngineeringDocument && (initial ? ids.Contains(d.Id) : d.IsCurrent)).OrderBy(d => d.UploadedAt).ThenBy(d => d.Id).ToArray();
        try { return new(await archive.CreateAsync(documents, token), initial ? "initial-handoff.zip" : "current.zip"); }
        catch (FileNotFoundException) { throw new DocumentException(500, "DocumentUnavailable"); }
        catch (DirectoryNotFoundException) { throw new DocumentException(500, "DocumentUnavailable"); }
    }
}
