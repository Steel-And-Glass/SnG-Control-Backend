using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class MeasurementChecklistConfiguration : IEntityTypeConfiguration<MeasurementChecklist>
{
    public void Configure(EntityTypeBuilder<MeasurementChecklist> b)
    {
        b.ToTable("MeasurementChecklists", t => {
            t.HasCheckConstraint("CK_MeasurementChecklists_State", "\"Revision\" >= 0 AND \"QuotationRound\" > 0 AND ((\"SubmissionId\" IS NULL AND \"SubmittedAt\" IS NULL AND \"SubmittedBySubject\" IS NULL AND \"SnapshotJson\" IS NULL) OR (\"SubmissionId\" IS NOT NULL AND \"SubmittedAt\" >= \"CreatedAt\" AND \"SubmittedBySubject\" IS NOT NULL AND \"SnapshotJson\" IS NOT NULL))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.Id, x.ProjectId });
        b.HasIndex(x => x.ProjectId).IsUnique(); b.HasIndex(x => x.TaskId).IsUnique(); b.HasIndex(x => x.SubmissionId).IsUnique();
        b.Property(x => x.Revision).IsConcurrencyToken(); b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CommercialQuotation>().WithMany().HasForeignKey(x => x.CommercialQuotationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class MeasurementStructureConfiguration : IEntityTypeConfiguration<MeasurementStructure>
{
    public void Configure(EntityTypeBuilder<MeasurementStructure> b)
    {
        b.ToTable("MeasurementStructures", t => {
            t.HasCheckConstraint("CK_MeasurementStructures_Actual", "(\"ActualWidth\" IS NULL AND \"ActualHeight\" IS NULL AND \"ActualSystemText\" IS NULL) OR (\"ActualWidth\" IS NOT NULL AND \"ActualWidth\" > 0 AND \"ActualHeight\" IS NOT NULL AND \"ActualHeight\" > 0 AND \"ActualSystemText\" IS NOT NULL AND length(btrim(\"ActualSystemText\")) > 0)");
            t.HasCheckConstraint("CK_MeasurementStructures_Unit", "\"UnitOrdinal\" > 0 AND \"UnitOrdinal\" <= \"UnitCount\" AND (\"CommercialQuotationItemId\" IS NOT NULL OR (\"UnitOrdinal\" = 1 AND \"UnitCount\" = 1 AND \"Width\" > 0 AND \"Height\" > 0))");
            t.HasCheckConstraint("CK_MeasurementStructures_State", "((\"MeasuredAt\" IS NULL AND \"MeasuredBySubject\" IS NULL) OR (\"MeasuredAt\" >= \"CreatedAt\" AND \"MeasuredBySubject\" IS NOT NULL)) AND ((\"RetiredAt\" IS NULL AND \"RetiredBySubject\" IS NULL) OR (\"RetiredAt\" >= \"CreatedAt\" AND \"RetiredBySubject\" IS NOT NULL))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.Comparison); b.Ignore(x => x.Modification); b.Ignore(x => x.ComparisonCurrent); b.Ignore(x => x.WidthDifferenceMm); b.Ignore(x => x.HeightDifferenceMm); b.Ignore(x => x.ComparisonStatus); b.Ignore(x => x.ModificationComplete); b.Ignore(x => x.AssessmentComplete);
        b.Property(x => x.ComparisonJson).HasColumnType("jsonb"); b.Property(x => x.ModificationJson).HasColumnType("jsonb");
        b.Ignore(x => x.Technical); b.Ignore(x => x.IsTechnicallyComplete);
        b.Property(x => x.TechnicalJson).HasColumnType("jsonb");
        b.Property(x => x.ActualSystemText).HasMaxLength(2000);
        b.Property(x => x.MeasurementType).HasMaxLength(20);
        b.Property(x => x.ActualLocation).HasMaxLength(500);
        b.Property(x => x.InstallationObservations).HasMaxLength(4000);
        b.HasAlternateKey(x => new { x.Id, x.ChecklistId });
        b.HasOne<MeasurementChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CommercialQuotationItem>().WithMany().HasForeignKey(x => x.CommercialQuotationItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ChecklistId, x.CommercialQuotationItemId, x.UnitOrdinal }).IsUnique().HasFilter("\"CommercialQuotationItemId\" IS NOT NULL");
    }
}
public sealed class MeasurementEventConfiguration : IEntityTypeConfiguration<MeasurementEvent>
{
    public void Configure(EntityTypeBuilder<MeasurementEvent> b)
    {
        b.ToTable("MeasurementEvents"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<MeasurementChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeasurementStructure>().WithMany().HasForeignKey(x => new { x.StructureId, x.ChecklistId }).HasPrincipalKey(x => new { x.Id, x.ChecklistId }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ChecklistId, x.RequestKey }).IsUnique(); b.HasIndex(x => new { x.ChecklistId, x.Revision }).IsUnique();
        b.Property(x => x.AssessmentChangeJson).HasColumnType("jsonb");
        b.Property(x => x.TechnicalChangeJson).HasColumnType("jsonb");
        b.Property(x => x.SketchChangeJson).HasColumnType("jsonb");
        b.Property(x => x.DetailsJson).HasColumnType("jsonb");
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Action).HasMaxLength(30);
    }
}
public sealed class MeasurementSubmissionDocumentConfiguration : IEntityTypeConfiguration<MeasurementSubmissionDocument>
{
    public void Configure(EntityTypeBuilder<MeasurementSubmissionDocument> b)
    {
        b.ToTable("MeasurementSubmissionDocuments"); b.HasKey(x => new { x.ChecklistId, x.ProjectDocumentId });
        b.HasOne<MeasurementChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ProjectDocumentId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MeasurementSketchVersionConfiguration : IEntityTypeConfiguration<MeasurementSketchVersion>
{
    public void Configure(EntityTypeBuilder<MeasurementSketchVersion> b)
    {
        b.ToTable("MeasurementSketchVersions"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.StorageKey); b.Property(x => x.DrawingJson).HasColumnType("jsonb"); b.Property(x => x.WebpSha256).HasMaxLength(64);
        b.HasAlternateKey(x => new { x.Id, x.StructureId });
        b.HasIndex(x => new { x.StructureId, x.Revision }).IsUnique();
        b.HasOne<MeasurementStructure>().WithMany().HasForeignKey(x => new { x.StructureId, x.ChecklistId }).HasPrincipalKey(x => new { x.Id, x.ChecklistId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeasurementChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MeasurementReportConfiguration : IEntityTypeConfiguration<MeasurementReport>
{
    public void Configure(EntityTypeBuilder<MeasurementReport> b)
    {
        b.ToTable("MeasurementReports"); b.HasKey(x => x.ChecklistId); b.Property(x => x.ChecklistId).ValueGeneratedNever();
        b.HasIndex(x => x.SubmissionId).IsUnique(); b.Property(x => x.PdfSha256).HasMaxLength(64); b.Property(x => x.SnapshotSha256).HasMaxLength(64);
        b.HasOne<MeasurementChecklist>().WithOne().HasForeignKey<MeasurementReport>(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey<MeasurementChecklist>(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
    }
}
