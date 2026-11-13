using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities.Incremental;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
internal static class ProductionPreparationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<DocumentExtractionJob>(b => {
            b.ToTable("DocumentExtractionJobs",t=> {
                t.HasCheckConstraint("CK_ExtractionJob_State","\"State\" IN (1,2,3,4,5) AND \"Attempts\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                t.HasCheckConstraint("CK_ExtractionJob_Lease","(\"State\"=2 AND \"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"State\"<>2 AND \"LeaseToken\" IS NULL AND \"LeaseUntil\" IS NULL)");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x=>x.PreparationId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>new{x.PreparationId,x.ExtractorVersion,x.Generation}).IsUnique();
            b.HasAlternateKey(x=>new{x.Id,x.PreparationId});b.Property(x=>x.ReextractReason).HasMaxLength(2000);b.Property(x=>x.Generation).HasDefaultValue(1);
            b.HasOne<ProductionComponentRevision>().WithMany().HasForeignKey(x=>new{x.ComponentRevisionId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.Property(x=>x.ExtractorVersion).HasMaxLength(100);b.Property(x=>x.RequestedBySubject).HasMaxLength(200);
            b.Property(x=>x.ErrorCode).HasMaxLength(100);b.Property(x=>x.ReportJson).HasColumnType("jsonb");b.Property(x=>x.EventsJson).HasColumnType("jsonb");b.Property(x=>x.Revision).IsConcurrencyToken();
        });
        model.Entity<ProductionPreparation>(b => {
            b.ToTable("ProductionPreparations", t => { t.HasCheckConstraint("CK_ProductionPreparation_State", "\"State\" IN (1,2,3,4)"); t.HasCheckConstraint("CK_ProductionPreparation_Revision", "\"Revision\" >= 0"); });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.Id, x.ReleaseVersionId });
            b.HasIndex(x => x.ReleaseVersionId).IsUnique();
            b.HasOne<OpeningFilingRelease>().WithMany().HasForeignKey(x => x.ReleaseVersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningFilingWorkspace>().WithMany().HasForeignKey(x => new { x.FilingId, x.ProjectId }).HasPrincipalKey(x => new { x.FilingId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.ManifestSha256).HasMaxLength(64); b.Property(x => x.Revision).IsConcurrencyToken();
            b.Property(x => x.CreatedBySubject).HasMaxLength(200); b.Property(x => x.ReviewedBySubject).HasMaxLength(200); b.Property(x => x.ExtractorVersion).HasMaxLength(100);
        });
        model.Entity<PreparationSourceMember>(b => {
            b.ToTable("PreparationSourceMembers"); b.HasKey(x => new { x.PreparationId, x.StructureId });
            b.HasAlternateKey(x => new { x.PreparationId, x.StructureId, x.Op });
            b.HasIndex(x => new { x.PreparationId, x.Op }).IsUnique(); b.Property(x => x.Op).HasMaxLength(80);
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x => new { x.PreparationId, x.ReleaseVersionId }).HasPrincipalKey(x => new { x.Id, x.ReleaseVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningFilingMember>().WithMany().HasForeignKey(x => new { x.ReleaseVersionId, x.StructureId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PreparationSourceDocument>(b => {
            b.ToTable("PreparationSourceDocuments"); b.HasKey(x => new { x.PreparationId, x.DocumentId });
            b.HasAlternateKey(x => new { x.PreparationId, x.DocumentId, x.Sha256 });
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x => new { x.PreparationId, x.ReleaseVersionId }).HasPrincipalKey(x => new { x.Id, x.ReleaseVersionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningFilingDocument>().WithMany().HasForeignKey(x => new { x.ReleaseVersionId, x.DocumentId }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Sha256).HasMaxLength(64); b.Property(x => x.Category).HasMaxLength(40);
        });
        model.Entity<ProductionComponentRevision>(b => {
            b.ToTable("ProductionComponentRevisions", t => { t.HasCheckConstraint("CK_ComponentRevision_State", "\"State\" IN (1,2,3,4)"); t.HasCheckConstraint("CK_ComponentRevision_Number", "\"Number\" > 0 AND \"Revision\" >= 0"); });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.Id, x.PreparationId });
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x => x.PreparationId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.PreparationId, x.Number }).IsUnique(); b.Property(x => x.Revision).IsConcurrencyToken();
            b.Property(x => x.CreatedBySubject).HasMaxLength(200); b.Property(x => x.ReviewedBySubject).HasMaxLength(200);
        });
        model.Entity<ProductionCandidateRow>(b => {
            b.ToTable("ProductionCandidateRows", t => {
                t.HasCheckConstraint("CK_Candidate_Quantity", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                t.HasCheckConstraint("CK_Candidate_Page", "\"Page\" IS NULL OR \"Page\" > 0");
                t.HasCheckConstraint("CK_Candidate_Route", "((\"Material\" IS NULL AND \"ProposedRoute\" IS NULL) OR (\"Material\" = 1 AND \"ProposedRoute\" = 1) OR (\"Material\" IN (2,3) AND \"ProposedRoute\" = 2)) IS TRUE");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.Id,x.PreparationId});
            b.HasOne<ProductionComponentRevision>().WithMany().HasForeignKey(x => new { x.ComponentRevisionId, x.PreparationId }).HasPrincipalKey(x => new { x.Id, x.PreparationId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PreparationSourceMember>().WithMany().HasForeignKey(x => new { x.PreparationId, x.StructureId, x.Op }).HasPrincipalKey(x => new { x.PreparationId, x.StructureId, x.Op }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PreparationSourceDocument>().WithMany().HasForeignKey(x => new { x.PreparationId, x.DocumentId, x.DocumentSha256 }).HasPrincipalKey(x => new { x.PreparationId, x.DocumentId, x.Sha256 }).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Op).HasMaxLength(80); b.Property(x => x.DocumentSha256).HasMaxLength(64);
            b.Property(x => x.SpecificationsJson).HasColumnType("jsonb"); b.Property(x => x.DiagnosticsJson).HasColumnType("jsonb"); b.Property(x => x.CoordinatesJson).HasColumnType("jsonb");
            foreach (var name in new[] { "Measure", "CutDimension", "Width", "Height", "LeftAngle", "RightAngle" }) b.Property<decimal?>(name).HasColumnType("numeric");
            foreach (var name in new[] { "ExtractorVersion", "TemplateVersion", "SourceRow", "Sheet", "OriginalPieceId", "Reference", "DeclaredMaterial" }) b.Property<string>(name).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(30); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.OriginalDimensions).HasMaxLength(1000);
            // Deliberately no uniqueness by dimensions, description or PieceId: identical legitimate rows remain distinct.
        });
    }
}
