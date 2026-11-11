using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
internal static class OpeningFilingConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<OpeningFilingWorkspace>(b => {
            b.ToTable("OpeningFilingWorkspaces"); b.HasKey(x => x.FilingId); b.Property(x => x.FilingId).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.FilingId, x.ProjectId });
            b.HasOne<OpeningFilingDelivery>().WithMany().HasForeignKey(x => new { x.FilingId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.ResponsibleSubject).HasMaxLength(200); b.Property(x => x.Revision).IsConcurrencyToken();
            b.Property(x => x.NumberOriginal).HasMaxLength(80); b.Property(x => x.NumberNormalized).HasMaxLength(80);
            b.HasIndex(x => new { x.ProjectId, x.NumberNormalized }).IsUnique().HasFilter("\"NumberNormalized\" IS NOT NULL");
        });
        model.Entity<OpeningFilingVersion>(b => {
            b.ToTable("OpeningFilingVersions"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.Id, x.ProjectId });
            b.HasOne<OpeningFilingWorkspace>().WithMany().HasForeignKey(x => new { x.FilingId, x.ProjectId }).HasPrincipalKey(x => new { x.FilingId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.FilingId, x.Number }).IsUnique();
            b.HasIndex(x => x.FilingId).IsUnique().HasFilter("\"ConfirmedAt\" IS NULL");
            b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.CreatedBySubject).HasMaxLength(200);
            b.Property(x => x.ConfirmedBySubject).HasMaxLength(200); b.Property(x => x.ManifestSha256).HasMaxLength(64);
        });
        model.Entity<OpeningFilingMember>(b => {
            b.ToTable("OpeningFilingMembers"); b.HasKey(x => new { x.VersionId, x.StructureId });
            b.HasOne<OpeningFilingVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementStructure>().WithMany().HasForeignKey(x => new { x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.OpOriginal).HasMaxLength(80); b.Property(x => x.OpNormalized).HasMaxLength(80);
            b.HasIndex(x => new { x.VersionId, x.OpNormalized }).IsUnique();
        });
        model.Entity<OpeningFilingDocument>(b => {
            b.ToTable("OpeningFilingDocuments"); b.HasKey(x => new { x.VersionId, x.DocumentId });
            b.HasOne<OpeningFilingVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.DocumentId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Category).HasMaxLength(40); b.Property(x => x.State).HasMaxLength(20); b.Property(x => x.Sha256).HasMaxLength(64);
            b.HasIndex(x => new { x.VersionId, x.LineageId }).IsUnique().HasFilter("\"State\" = 'Current'");
        });
        model.Entity<OpeningFilingEvent>(b => {
            b.ToTable("OpeningFilingEvents"); b.HasKey(x => new { x.ProjectId, x.RequestKey });
            b.HasOne<OpeningFilingWorkspace>().WithMany().HasForeignKey(x => new { x.FilingId, x.ProjectId }).HasPrincipalKey(x => new { x.FilingId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.ActorSubject).HasMaxLength(200); b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Action).HasMaxLength(40);
        });
        model.Entity<OpeningFilingRelease>(b => {
            b.ToTable("OpeningFilingReleases"); b.HasKey(x => x.VersionId); b.Property(x => x.VersionId).ValueGeneratedNever();
            b.HasOne<OpeningFilingVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningFilingWorkspace>().WithMany().HasForeignKey(x => new { x.FilingId, x.ProjectId }).HasPrincipalKey(x => new { x.FilingId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Sha256).HasMaxLength(64);
        });
    }
}
