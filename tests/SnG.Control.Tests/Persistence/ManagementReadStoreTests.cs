using Microsoft.EntityFrameworkCore;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.Persistence;

public class ManagementReadStoreTests
{
    [Fact]
    public void TaskQuery_IsOneUnfilteredSelectWithBothJoins_AndNoTracking()
    {
        using var db = new SnGControlDbContext(new DbContextOptionsBuilder<SnGControlDbContext>().UseNpgsql().Options);
        var query = new EfManagementReadStore(db).TasksQuery();
        var sql = query.ToQueryString();
        Assert.Contains("FROM \"ProjectTasks\"", sql);
        Assert.Contains("INNER JOIN \"Projects\"", sql); Assert.Contains("INNER JOIN \"Clients\"", sql);
        Assert.DoesNotContain("WHERE", sql); Assert.DoesNotContain("LIMIT", sql);
        Assert.Equal(1, sql.Split("SELECT").Length - 1);
        Assert.Equal(3, query.Expression.ToString().Split("AsNoTracking()").Length - 1);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
