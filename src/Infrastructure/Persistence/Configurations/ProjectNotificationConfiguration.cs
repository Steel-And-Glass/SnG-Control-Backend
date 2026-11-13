using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class ProjectNotificationConfiguration : IEntityTypeConfiguration<ProjectNotification>
{
    public const string EventKeyIndex = "UX_ProjectNotifications_EventKey";
    public void Configure(EntityTypeBuilder<ProjectNotification> builder)
    {
        builder.ToTable("ProjectNotifications", table =>
        {
            table.HasCheckConstraint("CK_ProjectNotifications_Text", "length(btrim(\"RecipientGoogleSubject\")) > 0 AND length(btrim(\"Title\")) > 0 AND length(btrim(\"Message\")) > 0 AND length(btrim(\"EventKey\")) > 0");
            table.HasCheckConstraint("CK_ProjectNotifications_Kind", "\"Kind\" IN (1, 2, 3, 4, 5, 6, 7, 8)");
            table.HasCheckConstraint("CK_ProjectNotifications_State", "(\"Kind\" <> 4 AND \"State\" = 1 AND \"ResolvedAt\" IS NULL) OR (\"Kind\" = 4 AND ((\"State\" = 2 AND \"ResolvedAt\" IS NULL) OR (\"State\" = 3 AND \"ResolvedAt\" IS NOT NULL)))");
        });
        builder.HasKey(n => n.Id); builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.RecipientGoogleSubject).IsRequired(); builder.Property(n => n.Title).IsRequired();
        builder.Property(n => n.Message).IsRequired(); builder.Property(n => n.EventKey).IsRequired();
        builder.Property(n => n.Kind).HasConversion<int>(); builder.Property(n => n.State).HasConversion<int>();
        builder.Property(n => n.ScheduledAt).HasColumnType("timestamp with time zone");
        builder.Property(n => n.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(n => n.ResolvedAt).HasColumnType("timestamp with time zone");
        builder.HasOne<Project>().WithMany().HasForeignKey(n => n.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectTask>().WithMany().HasForeignKey(n => n.ProjectTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => n.EventKey).IsUnique().HasDatabaseName(EventKeyIndex);
        builder.HasIndex(n => new { n.RecipientGoogleSubject, n.CreatedAt });
        builder.HasIndex(n => new { n.ProjectId, n.CreatedAt });
        builder.HasIndex(n => n.ProjectTaskId);
    }
}
