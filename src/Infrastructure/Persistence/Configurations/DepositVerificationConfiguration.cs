using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class DepositVerificationConfiguration : IEntityTypeConfiguration<DepositVerification>
{
    public void Configure(EntityTypeBuilder<DepositVerification> b)
    {
        b.ToTable("DepositVerifications", t => {
            t.HasCheckConstraint("CK_DepositVerifications_State", "(\"Status\" = 1 AND \"DecidedAt\" IS NULL AND \"DecidedByGoogleSubject\" IS NULL AND \"Note\" IS NULL) OR (\"Status\" IN (2,3) AND \"DecidedAt\" IS NOT NULL AND \"DecidedByGoogleSubject\" IS NOT NULL AND \"DecidedAt\" >= \"SubmittedAt\" AND length(btrim(\"DecidedByGoogleSubject\")) > 0 AND (\"Status\" <> 3 OR (\"Note\" IS NOT NULL AND length(btrim(\"Note\")) > 0)))");
            t.HasCheckConstraint("CK_DepositVerifications_Submitter", "length(btrim(\"SubmittedByGoogleSubject\")) > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Note).HasMaxLength(2000);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.CorrectionTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CorrectionTaskId).IsUnique();
        b.HasAlternateKey(x => new { x.Id, x.ProjectId });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingHandoff>().WithMany().HasForeignKey(x => x.AccountingHandoffId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.RequestKey }).IsUnique().HasDatabaseName("UX_DepositVerifications_Request");
        b.HasIndex(x => x.ProjectId, "UX_DepositVerifications_Pending").IsUnique().HasFilter("\"Status\" = 1");
        b.HasIndex(x => x.AccountingHandoffId, "UX_DepositVerifications_Approved").IsUnique().HasFilter("\"Status\" = 2");
    }
}
public sealed class DepositVerificationDocumentConfiguration : IEntityTypeConfiguration<DepositVerificationDocument>
{
    public void Configure(EntityTypeBuilder<DepositVerificationDocument> b)
    {
        b.ToTable("DepositVerificationDocuments");
        b.HasKey(x => new { x.DepositVerificationId, x.ProjectDocumentId });
        b.HasOne<DepositVerification>().WithMany().HasForeignKey(x => new { x.DepositVerificationId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ProjectDocumentId })
            .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
