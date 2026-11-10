using Microsoft.EntityFrameworkCore;
namespace SnG.Control.Infrastructure.Persistence;

/// <summary>Projects legacy writes into the additive model inside the caller's transaction.
/// Does not authorize or enable incremental operations, change old rows or promote incomplete evidence.</summary>
internal static class IncrementalLegacyProjection
{
    internal static Task SyncAsync(SnGControlDbContext db, Guid projectId, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Legacy projection requires the workflow transaction.");
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT sng_incremental_legacy_project({projectId})", token);
    }
}
