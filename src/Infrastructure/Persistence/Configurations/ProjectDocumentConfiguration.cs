using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> b)
    {
        b.ToTable("ProjectDocuments", t =>
        {
            t.HasCheckConstraint("CK_ProjectDocuments_Size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 100000000");
            t.HasCheckConstraint("CK_ProjectDocuments_Text", "length(btrim(\"OriginalFileName\")) > 0 AND length(btrim(\"StorageKey\")) > 0 AND length(btrim(\"UploadedByGoogleSubject\")) > 0");
            t.HasCheckConstraint("CK_ProjectDocuments_State", "((\"ReplacedAt\" IS NULL AND \"ReplacedByDocumentId\" IS NULL) OR (\"ReplacedAt\" IS NOT NULL AND \"ReplacedAt\" >= \"UploadedAt\" AND \"ReplacedByDocumentId\" IS NOT NULL AND \"ReplacedByDocumentId\" <> \"Id\" AND \"RemovedAt\" IS NULL)) AND (\"RemovedAt\" IS NULL OR (\"RemovedAt\" >= \"UploadedAt\" AND \"ReplacedAt\" IS NULL))");
        });
        b.HasKey(d => d.Id); b.Property(d => d.Id).ValueGeneratedNever();
        b.HasAlternateKey(d => new { d.ProjectId, d.Id });
        b.Ignore(d => d.IsCurrent);
        b.Property(d => d.OriginalFileName).HasMaxLength(ProjectDocument.MaxFileNameLength).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(255);
        b.Property(d => d.StorageKey).IsRequired(); b.Property(d => d.UploadedByGoogleSubject).IsRequired();
        b.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithMany().HasForeignKey(d => new { d.ProjectId, d.ReplacedByDocumentId })
            .HasPrincipalKey(d => new { d.ProjectId, d.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(d => d.StorageKey).IsUnique();
        b.HasIndex(d => new { d.ProjectId, d.UploadedAt });
        b.HasIndex(d => d.ProjectId).HasFilter("\"ReplacedAt\" IS NULL AND \"RemovedAt\" IS NULL").HasDatabaseName("IX_ProjectDocuments_Current");
    }
}

public sealed class ProjectDocumentHandoffConfiguration : IEntityTypeConfiguration<ProjectDocumentHandoff>
{
    public void Configure(EntityTypeBuilder<ProjectDocumentHandoff> b)
    {
        b.ToTable("ProjectDocumentHandoffs", t => t.HasCheckConstraint("CK_ProjectDocumentHandoffs_Subject", "length(btrim(\"SentByGoogleSubject\")) > 0"));
        b.HasKey(h => h.Id); b.Property(h => h.Id).ValueGeneratedNever();
        b.Property(h => h.SentByGoogleSubject).IsRequired();
        b.HasAlternateKey(h => new { h.ProjectId, h.Id });
        b.HasIndex(h => h.ProjectId).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(h => h.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProjectDocumentHandoffItemConfiguration : IEntityTypeConfiguration<ProjectDocumentHandoffItem>
{
    public void Configure(EntityTypeBuilder<ProjectDocumentHandoffItem> b)
    {
        b.ToTable("ProjectDocumentHandoffItems");
        b.HasKey(i => new { i.HandoffId, i.ProjectDocumentId });
        b.HasOne<ProjectDocumentHandoff>().WithMany().HasForeignKey(i => new { i.ProjectId, i.HandoffId })
            .HasPrincipalKey(h => new { h.ProjectId, h.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithMany().HasForeignKey(i => new { i.ProjectId, i.ProjectDocumentId })
            .HasPrincipalKey(d => new { d.ProjectId, d.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
