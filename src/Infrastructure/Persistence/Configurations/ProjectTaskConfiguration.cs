using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    internal const string PrepareQuotationIndexName = "UX_ProjectTasks_ProjectId_PrepareQuotation";
    internal const string ReviewQuotationIndexName = "UX_ProjectTasks_ProjectId_ReviewQuotation";
    internal const string PresentQuotationIndexName = "UX_ProjectTasks_ProjectId_PresentQuotationToClient";

    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_EngineeringPackage", "\"Type\" <> 11 OR (\"Area\"=5 AND \"QuotationRound\" IS NULL AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)"));
        builder.ToTable("ProjectTasks", t => t.HasCheckConstraint("CK_ProjectTasks_QuotationRound", "\"Type\" NOT IN (1,2,3) OR (\"QuotationRound\" IS NOT NULL AND \"QuotationRound\" > 0)"));
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_Accounting", "\"Type\" <> 4 OR (\"QuotationRound\" IS NULL AND \"Area\" = 3 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)"));
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_Installation", "\"Type\" <> 5 OR (\"QuotationRound\" IS NULL AND \"Area\" = 4 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)"));
        builder.HasIndex(task => task.ProjectId, "UX_ProjectTasks_Measurements").IsUnique().HasFilter("\"Type\" = 5");
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_DepositCorrection", "\"Type\" <> 6 OR (\"QuotationRound\" IS NULL AND \"Area\" = 1 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)"));
        builder.HasIndex(task => task.ProjectId, "UX_ProjectTasks_ActiveDepositCorrection").IsUnique().HasFilter("\"Type\" = 6 AND \"Status\" IN (1,2)");
        builder.Property(task => task.DeadlineAt).HasColumnType("timestamp with time zone");
        builder.Property(task => task.WasOverdue);
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_Engineering", """ "Type" <> 7 OR ("QuotationRound" IS NULL AND "Area" = 5 AND "DeadlineAt" IS NULL AND "WasOverdue" IS NULL) """));
        builder.HasIndex(task => task.ProjectId, "UX_ProjectTasks_Engineering").IsUnique().HasFilter(""" "Type" = 7 """);
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_OpeningRounds", "\"Type\" NOT IN (8,9,10) OR (\"QuotationRound\" IS NULL AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL AND ((\"Type\" = 8 AND \"Area\" = 1) OR (\"Type\" IN (9,10) AND \"Area\" = 5)))"));
        builder.HasIndex(task => task.ProjectId, "UX_ProjectTasks_FileOpeningRecord").IsUnique().HasFilter("\"Type\" = 10 AND \"IncrementalAcceptanceId\" IS NULL");
        builder.HasIndex(t => t.IncrementalAcceptanceId).IsUnique();
        builder.HasOne<SnG.Control.Domain.Entities.Incremental.OpeningAcceptance>().WithMany()
            .HasForeignKey(t => new { t.IncrementalAcceptanceId, t.ProjectId }).HasPrincipalKey(a => new { a.Id, a.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_ProjectTasks_IncrementalAcceptance", "\"IncrementalAcceptanceId\" IS NULL OR \"Type\" = 10"));
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Property(task => task.ProjectId).IsRequired();
        builder.Property(task => task.AssignedToGoogleSubject).IsRequired(false);
        builder.Property(task => task.Area).HasConversion<int>().IsRequired();
        builder.Property(task => task.Type).HasConversion<int>().IsRequired();
        builder.Property(task => task.Status).HasConversion<int>().IsRequired();
        builder.Property(task => task.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(task => task.StartedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(task => task.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(task => task.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(task => task.ProjectId);
        builder.HasIndex(task => new { task.Area, task.Status });
        builder.HasIndex(task => new { task.ProjectId, task.QuotationRound }, PrepareQuotationIndexName)
            .IsUnique()
            .HasFilter("\"Type\" = 1");
        builder.HasIndex(task => new { task.ProjectId, task.QuotationRound }, ReviewQuotationIndexName)
            .IsUnique().HasFilter("\"Type\" = 2");
        builder.HasIndex(task => new { task.ProjectId, task.QuotationRound }, PresentQuotationIndexName)
            .IsUnique().HasFilter("\"Type\" = 3");
    }
}
