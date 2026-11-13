using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
public sealed class EngineeringChecklistConfiguration : IEntityTypeConfiguration<EngineeringChecklist>
{
    public void Configure(EntityTypeBuilder<EngineeringChecklist> b)
    {
        b.ToTable("EngineeringChecklists", t => t.HasCheckConstraint("CK_EngineeringChecklist_State", """ "Revision" >= 0 AND (("CompletedAt" IS NULL AND "CompletedBySubject" IS NULL AND "FinalSnapshotJson" IS NULL) OR ("CompletedAt" >= "CreatedAt" AND "CompletedBySubject" IS NOT NULL AND "FinalSnapshotJson" IS NOT NULL AND "CurrentDocumentId" IS NOT NULL)) """));
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.Id, x.ProjectId });
        b.HasIndex(x => x.SubmissionId).IsUnique(); b.HasIndex(x => x.ProjectId).IsUnique(); b.HasIndex(x => x.TaskId).IsUnique();
        b.Property(x => x.Revision).IsConcurrencyToken(); b.Property(x => x.FinalSnapshotJson).HasColumnType("jsonb");
        b.HasOne<MeasurementChecklist>().WithOne().HasForeignKey<EngineeringChecklist>(x => new { x.MeasurementChecklistId, x.ProjectId }).HasPrincipalKey<MeasurementChecklist>(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithOne().HasForeignKey<EngineeringChecklist>(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.ProjectId, x.CurrentDocumentId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class EngineeringStructureConfiguration : IEntityTypeConfiguration<EngineeringStructure>
{
    public void Configure(EntityTypeBuilder<EngineeringStructure> b)
    {
        b.ToTable("EngineeringStructures", t => t.HasCheckConstraint("CK_EngineeringStructure_State", """ "Position" >= 0 AND "Revision" >= 0 AND (NOT "Completed" OR ("ChangedAt" IS NOT NULL AND "ChangedBySubject" IS NOT NULL)) """));
        b.HasKey(x => new { x.ChecklistId, x.StructureId }); b.HasIndex(x => new { x.ChecklistId, x.Position }).IsUnique();
        b.Property(x => x.Revision).IsConcurrencyToken(); b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        b.HasOne<EngineeringChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeasurementStructure>().WithMany().HasForeignKey(x => x.StructureId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class EngineeringEventConfiguration : IEntityTypeConfiguration<EngineeringEvent>
{
    public void Configure(EntityTypeBuilder<EngineeringEvent> b)
    {
        b.ToTable("EngineeringEvents"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.ChecklistId, x.RequestKey }).IsUnique(); b.HasIndex(x => new { x.ChecklistId, x.Revision }).IsUnique();
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Action).HasMaxLength(30); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<EngineeringChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class EngineeringActaConfiguration : IEntityTypeConfiguration<EngineeringActa>
{
    public void Configure(EntityTypeBuilder<EngineeringActa> b)
    {
        b.ToTable("EngineeringActas", t => t.HasCheckConstraint("CK_EngineeringActa_Version", """ "Version" > 0 """));
        b.HasKey(x => x.DocumentId); b.Property(x => x.DocumentId).ValueGeneratedNever(); b.Property(x => x.Sha256).HasMaxLength(64);
        b.HasIndex(x => new { x.ChecklistId, x.Version }).IsUnique();
        b.HasOne<EngineeringChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithOne().HasForeignKey<EngineeringActa>(x => new { x.ProjectId, x.DocumentId }).HasPrincipalKey<ProjectDocument>(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
