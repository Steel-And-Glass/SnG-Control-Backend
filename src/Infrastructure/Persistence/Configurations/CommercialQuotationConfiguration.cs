using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class CommercialQuotationConfiguration : IEntityTypeConfiguration<CommercialQuotation>
{
    internal const string VersionIndexName = "UX_CommercialQuotations_BudgetQuotationVersionId";
    internal const string CatalogIndexName = "UX_CommercialQuotations_CommercialCatalogId";
    public void Configure(EntityTypeBuilder<CommercialQuotation> builder)
    {
        builder.ToTable("CommercialQuotations", table =>
        {
            table.HasCheckConstraint("CK_CommercialQuotations_ProjectTotal", "\"ProjectTotal\" >= 0");
            table.HasCheckConstraint("CK_CommercialQuotations_Subject", "length(btrim(\"ConfirmedByGoogleSubject\")) > 0");
        });
        builder.HasKey(q => q.Id); builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.ProjectTotal).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(q => q.ConfirmedAt).HasColumnType("timestamp with time zone");
        builder.HasOne<Project>().WithMany().HasForeignKey(q => q.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BudgetQuotationVersion>().WithMany().HasForeignKey(q => q.BudgetQuotationVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CommercialCatalog>().WithMany().HasForeignKey(q => q.CommercialCatalogId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(q => q.ProjectId);
        builder.HasIndex(q => q.BudgetQuotationVersionId).IsUnique().HasDatabaseName(VersionIndexName);
        builder.HasIndex(q => q.CommercialCatalogId).IsUnique().HasDatabaseName(CatalogIndexName);
        builder.HasIndex(q => q.FileStorageKey).IsUnique();
    }
}
