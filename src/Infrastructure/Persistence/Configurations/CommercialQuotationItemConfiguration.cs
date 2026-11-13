using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class CommercialQuotationItemConfiguration : IEntityTypeConfiguration<CommercialQuotationItem>
{
    public void Configure(EntityTypeBuilder<CommercialQuotationItem> builder)
    {
        builder.ToTable("CommercialQuotationItems", table =>
        {
            table.HasCheckConstraint("CK_CommercialQuotationItems_Structure", "\"SourceRow\" > 0 AND \"Section\" IN (1, 2)");
            table.HasCheckConstraint("CK_CommercialQuotationItems_Values", "\"Width\" > 0 AND \"Height\" > 0 AND \"SquareMeters\" > 0 AND \"Quantity\" > 0 AND length(btrim(\"ItemNumber\")) > 0");
            table.HasCheckConstraint("CK_CommercialQuotationItems_Prices", "\"UnitPrice\" >= 0 AND \"TotalPrice\" >= 0");
        });
        builder.HasKey(i => i.Id); builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Section).HasConversion<int>();
        builder.Property(i => i.UnitPrice).HasPrecision(Money.Precision, Money.Scale); builder.Property(i => i.TotalPrice).HasPrecision(Money.Precision, Money.Scale);
        builder.HasOne<CommercialQuotation>().WithMany().HasForeignKey(i => i.CommercialQuotationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CommercialCatalogItem>().WithMany().HasForeignKey(i => i.CommercialCatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.CommercialQuotationId, i.Section, i.SourceRow }).IsUnique();
        builder.HasIndex(i => new { i.CommercialQuotationId, i.CommercialCatalogItemId }).IsUnique();
    }
}
