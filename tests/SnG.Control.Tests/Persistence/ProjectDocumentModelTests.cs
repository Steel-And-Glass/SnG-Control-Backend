using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Persistence;

namespace SnG.Control.Tests.Persistence;

public class ProjectDocumentModelTests
{
    [Fact]
    public void Model_ProtectsHistoryProjectIsolationSizeStateAndUniqueHandoff()
    {
        using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var doc = model.FindEntityType(typeof(ProjectDocument))!;
        Assert.Equal(17, doc.GetProperties().Count()); Assert.Null(doc.FindProperty("IsCurrent"));
        Assert.Equal(255, doc.FindProperty("OriginalFileName")!.GetMaxLength());
        Assert.Contains(doc.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "StorageKey");
        Assert.DoesNotContain(doc.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == "OriginalFileName"));
        Assert.Contains(doc.GetIndexes(), i => i.GetFilter() == "\"ReplacedAt\" IS NULL AND \"RemovedAt\" IS NULL");
        Assert.Contains(doc.GetForeignKeys(), fk => fk.PrincipalEntityType == doc && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProjectId", "ReplacedByDocumentId" }));
        Assert.Equal(3, doc.GetCheckConstraints().Count());
        Assert.Contains("100000000", doc.GetCheckConstraints().Single(c => c.Name == "CK_ProjectDocuments_Size").Sql);
        Assert.Contains("\"ReplacedAt\" IS NOT NULL", doc.GetCheckConstraints().Single(c => c.Name == "CK_ProjectDocuments_State").Sql);
        var handoff = model.FindEntityType(typeof(ProjectDocumentHandoff))!;
        Assert.Contains(handoff.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "ProjectId");
        var item = model.FindEntityType(typeof(ProjectDocumentHandoffItem))!;
        Assert.Equal(2, item.GetForeignKeys().Count());
        Assert.All(item.GetForeignKeys(), fk => Assert.Equal("ProjectId", fk.Properties[0].Name));
        foreach (var entity in new[] { doc, handoff, item }) Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        var notification = model.FindEntityType(typeof(ProjectNotification))!;
        Assert.Contains("1, 2, 3, 4, 5", notification.GetCheckConstraints().Single(c => c.Name == "CK_ProjectNotifications_Kind").Sql);
        Assert.Contains("\"Kind\" <> 4 AND \"State\" = 1", notification.GetCheckConstraints().Single(c => c.Name == "CK_ProjectNotifications_State").Sql);
        Assert.Equal(5, (int)NotificationKind.ProjectDocumentsUpdated);
        Assert.Equal(32, db.Database.GetMigrations().Count());
        Assert.EndsWith("AddProductionHumanReview", db.Database.GetMigrations().Last());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
