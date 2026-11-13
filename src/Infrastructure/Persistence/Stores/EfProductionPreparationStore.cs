using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Infrastructure.Persistence.Stores;
public sealed class EfProductionPreparationStore(SnGControlDbContext db, TimeProvider clock) : IProductionPreparationStore
{
    private static PreparationSummary Summary(ProductionPreparation p) => new(p.Id, p.ProjectId, p.FilingId, p.ReleaseVersionId,
        p.ManifestSha256, p.CreatedAt, p.State.ToString(), p.Revision, p.ExtractorVersion, p.ReviewedBySubject);
    public async Task<IReadOnlyList<ReleaseSummary>> ReleasesAsync(int skip, CancellationToken token) => await
        (from r in db.Set<OpeningFilingRelease>().AsNoTracking()
         join p in db.Set<ProductionPreparation>().AsNoTracking() on r.VersionId equals p.ReleaseVersionId into preparations
         from p in preparations.DefaultIfEmpty() orderby r.At, r.VersionId
         select new ReleaseSummary(r.VersionId, r.ProjectId, r.FilingId, r.At, r.Sha256, p == null ? null : p.Id)).Skip(skip).Take(50).ToArrayAsync(token);
    public async Task<IReadOnlyList<PreparationSummary>> ListAsync(int skip, CancellationToken token) =>
        (await db.Set<ProductionPreparation>().AsNoTracking().OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Skip(skip).Take(50).ToArrayAsync(token)).Select(Summary).ToArray();
    public async Task<PreparationDetail?> ReadAsync(Guid id, CancellationToken token)
    {
        var p = await db.Set<ProductionPreparation>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        if (p == null) return null;
        var members = await db.Set<PreparationSourceMember>().AsNoTracking().Where(m => m.PreparationId == id).OrderBy(m => m.Op).Select(m => new PreparationMember(m.StructureId, m.Op)).ToArrayAsync(token);
        var docs = await db.Set<PreparationSourceDocument>().AsNoTracking().Where(d => d.PreparationId == id).OrderBy(d => d.DocumentId).Select(d => new PreparationDocument(d.DocumentId, d.Sha256, d.Category)).ToArrayAsync(token);
        return new(Summary(p), members, docs);
    }
    public async Task<PreparationDetail> CreateAsync(Guid releaseVersionId, string actor, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var release = await db.Set<OpeningFilingRelease>().AsNoTracking().SingleOrDefaultAsync(r => r.VersionId == releaseVersionId, token)
            ?? throw new DocumentException(404, "OperationsReleaseNotFound");
        // Same project lock order as operational workflows; serialize explicit discovery/retries.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Projects\" WHERE \"Id\" = {release.ProjectId} FOR UPDATE", token);
        var existing = await db.Set<ProductionPreparation>().AsNoTracking().SingleOrDefaultAsync(p => p.ReleaseVersionId == releaseVersionId, token);
        if (existing != null) { var found = (await ReadAsync(existing.Id, token))!; await tx.CommitAsync(token); return found; }
        var version = await db.Set<OpeningFilingVersion>().AsNoTracking().SingleAsync(v => v.Id == releaseVersionId, token);
        var members = await db.Set<OpeningFilingMember>().AsNoTracking().Where(m => m.VersionId == releaseVersionId).ToArrayAsync(token);
        var docs = await db.Set<OpeningFilingDocument>().AsNoTracking().Where(d => d.VersionId == releaseVersionId && d.State == "Current").ToArrayAsync(token);
        var native = await (from d in db.Set<OpeningFilingDelivery>() join a in db.Set<OpeningAcceptance>() on d.AcceptanceId equals a.Id
                            where d.Id == release.FilingId && d.ProjectId == release.ProjectId && !a.IsLegacy select d.Id).AnyAsync(token);
        if (!native || !ValidSource(release, version, members, docs)) throw new DocumentException(409, "OperationsReleaseEvidenceInvalid");
        var p = new ProductionPreparation(release, clock.GetUtcNow().UtcDateTime, actor);
        db.Add(p); db.AddRange(members.Select(m => new PreparationSourceMember(p, m))); db.AddRange(docs.Select(d => new PreparationSourceDocument(p, d)));
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return (await ReadAsync(p.Id, token))!;
    }
    internal static bool ValidSource(OpeningFilingRelease release, OpeningFilingVersion version, OpeningFilingMember[] members, OpeningFilingDocument[] docs)
    {
        try
        {
            if (version.ConfirmedAt == null || release.At != version.ConfirmedAt || release.ProjectId != version.ProjectId || release.FilingId != version.FilingId ||
                release.Canonical != version.ManifestCanonical || release.Sha256 != version.ManifestSha256 || IncrementalEvidence.Hash(release.Canonical) != release.Sha256 ||
                members.Length == 0 || members.Any(m => !m.Confirmed) || OpeningFilingRules.Categories.Take(7).Any(c => !docs.Any(d => d.Category == c))) return false;
            using var json = JsonDocument.Parse(release.Canonical); var root = json.RootElement;
            if (root.GetProperty("ProjectId").GetGuid() != release.ProjectId || root.GetProperty("FilingId").GetGuid() != release.FilingId || root.GetProperty("VersionId").GetGuid() != release.VersionId) return false;
            var mm = root.GetProperty("Structures").EnumerateArray().ToArray(); var dd = root.GetProperty("Documents").EnumerateArray().ToArray();
            var evidence = root.GetProperty("Evidence").EnumerateArray().ToArray();
            return mm.Length == members.Length && mm.Select(m => m.GetProperty("StructureId").GetGuid()).Distinct().Count() == mm.Length &&
                mm.All(m => members.Any(x => x.StructureId == m.GetProperty("StructureId").GetGuid() && x.OpNormalized == m.GetProperty("OpNormalized").GetString() && m.GetProperty("Confirmed").GetBoolean())) &&
                dd.Length == docs.Length && dd.Select(d => d.GetProperty("Id").GetGuid()).Distinct().Count() == dd.Length &&
                dd.All(d => docs.Any(x => x.DocumentId == d.GetProperty("Id").GetGuid() && x.Sha256 == d.GetProperty("Sha256").GetString() && x.Category == d.GetProperty("Category").GetString())) &&
                evidence.Length == members.Length && evidence.All(e => e.GetProperty("Attested").GetBoolean()) &&
                members.Select(m => m.StructureId).ToHashSet().SetEquals(evidence.Select(e => e.GetProperty("StructureId").GetGuid()));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }
}
