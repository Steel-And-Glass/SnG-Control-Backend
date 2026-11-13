using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class RequotationRequestConfiguration : IEntityTypeConfiguration<RequotationRequest>
{
    internal const string DecisionIndex = "UX_RequotationRequests_SourceDecision";
    internal const string TaskIndex = "UX_RequotationRequests_PrepareTask";
    internal const string RoundIndex = "UX_RequotationRequests_ProjectRound";
    public void Configure(EntityTypeBuilder<RequotationRequest> b)
    {
        b.ToTable("RequotationRequests", t =>
        {
            t.HasCheckConstraint("CK_RequotationRequests_Round", "\"TargetQuotationRound\" > 1");
            t.HasCheckConstraint("CK_RequotationRequests_Changes", "length(btrim(\"RequestedChanges\")) > 0");
            t.HasCheckConstraint("CK_RequotationRequests_Subject", "length(btrim(\"RequestedByGoogleSubject\")) > 0");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.RequestedChanges).HasMaxLength(4000);
        b.Property(r => r.RequestedAt).HasColumnType("timestamp with time zone");
        b.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ClientQuotationDecision>().WithMany().HasForeignKey(r => r.SourceClientQuotationDecisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(r => r.PrepareQuotationTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.SourceClientQuotationDecisionId).IsUnique().HasDatabaseName(DecisionIndex);
        b.HasIndex(r => r.PrepareQuotationTaskId).IsUnique().HasDatabaseName(TaskIndex);
        b.HasIndex(r => new { r.ProjectId, r.TargetQuotationRound }).IsUnique().HasDatabaseName(RoundIndex);
    }
}
