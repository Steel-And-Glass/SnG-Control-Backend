using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Access;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Engineering;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Enums;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed partial class EfOpeningFilingStore
{
    public async Task UploadAsync(Guid id, Guid filingId, OpeningFilingUpload manifest, IAsyncEnumerable<DocumentUpload> uploads, DecisionActor actor, CancellationToken token)
    {
        var initial = await Source(id, filingId, token); await Authorize(id, initial.Acceptance, initial.Task, actor, token);
        byte[]? bytes = null; string? name = null;
        await foreach (var upload in uploads.WithCancellation(token))
        {
            if (bytes != null || upload.ReplacesDocumentId != null || upload.ContentType != "application/pdf") throw new DocumentException(400, "InvalidEngineeringPdf");
            bytes = await EfEngineeringStore.ReadBounded(upload.Content, token); name = upload.FileName; EfEngineeringStore.ValidatePdf(name, bytes);
            if (bytes.LongLength > limits.Value.MaxFileBytes) throw new DocumentException(413, "OpeningFilingStorageLimit");
        }
        if (bytes == null) throw new DocumentException(400, "InvalidEngineeringPdf");
        var hash = Hash(bytes); var fp = Fingerprint(new { manifest, name, hash });
        await using var tx = await db.Database.BeginTransactionAsync(token); await Lock(id, token);
        var source = await Source(id, filingId, token); await Authorize(id, source.Acceptance, source.Task, actor, token);
        if (await Replay(id, filingId, manifest.RequestKey, fp, actor, token)) return;
        var used = await db.ProjectDocuments.Where(d => d.ProjectId == id && db.Set<OpeningFilingDocument>().Any(l => l.DocumentId == d.Id)).SumAsync(d => (long?)d.SizeBytes, token) ?? 0;
        if (used > limits.Value.MaxProjectStorageBytes - bytes.LongLength) throw new DocumentException(413, "OpeningFilingStorageLimit");
        var (w, v) = await Draft(source.Delivery, source.Task, actor, manifest.ExpectedRevision, token);
        var at = timing.UtcNow;
        var doc = new ProjectDocument(Guid.NewGuid(), id, name!, "application/pdf", bytes.Length, manifest.RequestKey, at, actor.Subject, actor.Email, actor.Name, isEngineeringDocument: true);
        Guid lineage = doc.Id; int fileVersion = 1;
        if (manifest.ReplacesDocumentId is { } previousId)
        {
            var previous = await db.Set<OpeningFilingDocument>().SingleOrDefaultAsync(d => d.VersionId == v.Id && d.DocumentId == previousId && d.Category == manifest.Category && d.State == "Current", token) ?? throw Conflict("OpeningFilingDocumentInvalid");
            lineage = previous.LineageId; fileVersion = previous.FileVersion + 1; previous.Retire(v, true);
            await db.SaveChangesAsync(token);
        }
        var link = new OpeningFilingDocument(v, doc.Id, manifest.Category, hash, lineage, fileVersion); w.Advance();
        db.AddRange(doc, link, new OpeningFilingEvent(id, filingId, manifest.RequestKey, fp, "Upload", at, actor.Subject, Json(new { VersionId = v.Id, doc.Id, manifest.Category, manifest.ReplacesDocumentId, hash, fileVersion })));
        var commitAttempted = false;
        try { await files.SaveAsync(doc.StorageKey, bytes, token); await db.SaveChangesAsync(token); commitAttempted = true; await tx.CommitAsync(token); }
        catch { if (!commitAttempted) { await tx.RollbackAsync(CancellationToken.None); await files.DeleteAsync(doc.StorageKey, CancellationToken.None); } throw; }
    }
    public async Task<DocumentDownload> DownloadAsync(Guid id, Guid filingId, Guid versionId, Guid documentId, ProjectReadScope scope, CancellationToken token)
    {
        if (!await CanRead(id, scope, token)) throw new DocumentException(403, "EngineeringAccessDenied");
        var doc = await (from link in db.Set<OpeningFilingDocument>().AsNoTracking()
            join v in db.Set<OpeningFilingVersion>().AsNoTracking() on link.VersionId equals v.Id
            join d in db.ProjectDocuments.AsNoTracking() on link.DocumentId equals d.Id
            where v.ProjectId == id && v.FilingId == filingId && v.Id == versionId && link.DocumentId == documentId && d.ProjectId == id
            select new { d, link.Sha256 }).SingleOrDefaultAsync(token) ?? throw new DocumentException(404, "DocumentNotFound");
        return new(new MemoryStream(await Verified(doc.d, doc.Sha256, token), false), doc.d.OriginalFileName);
    }
}
