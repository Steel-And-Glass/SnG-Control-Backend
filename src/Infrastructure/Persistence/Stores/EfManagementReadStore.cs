using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Management;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public sealed partial class EfManagementReadStore(SnGControlDbContext context) : IManagementReadStore
{
    public Task<int> CountProjectsAsync(CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().CountAsync(cancellationToken);

    internal IQueryable<ManagementTaskData> TasksQuery() =>
        from task in context.ProjectTasks.AsNoTracking()
        join project in context.Projects.AsNoTracking() on task.ProjectId equals project.Id
        join client in context.Clients.AsNoTracking() on project.ClientId equals client.Id
        select new ManagementTaskData(task, project.Name, client.Id, client.Name, project.SgCode, project.City, project.ProductLine);

    public async Task<IReadOnlyList<ManagementTaskData>> ListTasksAsync(CancellationToken cancellationToken) =>
        await TasksQuery().ToListAsync(cancellationToken);
}
