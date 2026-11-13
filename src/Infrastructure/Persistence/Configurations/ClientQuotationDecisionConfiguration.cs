using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class ClientQuotationDecisionConfiguration : IEntityTypeConfiguration<ClientQuotationDecision>
{
    internal const string QuotationIndex = "UX_ClientQuotationDecisions_CommercialQuotationId";
    internal const string TaskIndex = "UX_ClientQuotationDecisions_PresentQuotationTaskId";
    public void Configure(EntityTypeBuilder<ClientQuotationDecision> b)
    {
        b.ToTable("ClientQuotationDecisions", t =>
        {
            t.HasCheckConstraint("CK_ClientQuotationDecisions_Decision", "\"Decision\" IN (1, 2, 3)");
            t.HasCheckConstraint("CK_ClientQuotationDecisions_Subject", "length(btrim(\"RecordedByGoogleSubject\")) > 0");
        });
        b.HasKey(d => d.Id); b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Note).HasMaxLength(2000);
        b.Property(d => d.RecordedAt).HasColumnType("timestamp with time zone");
        b.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CommercialQuotation>().WithMany().HasForeignKey(d => d.CommercialQuotationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(d => d.PresentQuotationTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(d => d.CommercialQuotationId).IsUnique().HasDatabaseName(QuotationIndex);
        b.HasIndex(d => d.PresentQuotationTaskId).IsUnique().HasDatabaseName(TaskIndex);
    }
}
