using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    internal const string SgCodeIndexName = "IX_Projects_SgCode";

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", table => table.HasCheckConstraint(
            "CK_Projects_SgCode_Format", "\"SgCode\" IS NULL OR \"SgCode\" ~ '^SG[0-9]+$'"));
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.ClientId).IsRequired();
        builder.Property(project => project.Name).IsRequired();
        builder.Property(project => project.City).IsRequired();
        builder.Property(project => project.ProductLine).HasConversion<int>().IsRequired();
        builder.Property(project => project.SgCode).IsRequired(false);
        builder.Property(project => project.CreatedByGoogleSubject).IsRequired(false);
        builder.Property(project => project.CreatedByEmail).IsRequired(false);
        builder.Property(project => project.CreatedByName).IsRequired(false);
        builder.Property(project => project.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(project => project.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<Client>()
            .WithMany()
            .HasForeignKey(project => project.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(project => project.SgCode).IsUnique().HasDatabaseName(SgCodeIndexName);
    }
}
