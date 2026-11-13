global using SnG.Control.Tests.TestDoubles;
using SnG.Control.Application.Scheduling;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Domain.Entities;
using SnG.Control.Infrastructure.Persistence;
using SnG.Control.Infrastructure.Persistence.Stores;

namespace SnG.Control.Tests.TestDoubles;

public sealed class ControlledTimeProvider(DateTime utc) : TimeProvider
{
    public DateTime Now { get; set; } = utc;
    public override DateTimeOffset GetUtcNow() => new(Now);
}
public static class TestTiming
{
    public static TaskTiming Value { get; } = new(TimeProvider.System, new());
    // Compatibility helpers for pre-existing scenarios, restricted to the test assembly.
    public static void Start(this ProjectTask task) { var now = DateTime.UtcNow; task.Start(now, Value.Deadline(task.Type, now)); }
    public static void Complete(this ProjectTask task) => task.Complete(DateTime.UtcNow);
}
public sealed class NotificationWriterDouble : EfProjectNotificationWriter
{
    private readonly SnGControlDbContext context;
    public NotificationWriterDouble(SnGControlDbContext context) : base(context, new ProjectNotificationPlanner(TestTiming.Value)) => this.context = context;
    protected override Task<List<ProjectNotification>> LoadAsync(Guid[] ids, CancellationToken token) =>
        Task.FromResult(context.ProjectNotifications.Local.Where(n => ids.Contains(n.ProjectTaskId)).ToList());
}
