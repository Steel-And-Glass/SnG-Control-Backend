using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Projects;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public class EfProjectStore(SnGControlDbContext context) : IProjectStore
{
    public Task<Project?> GetReadableByIdAsync(Guid id, SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        context.Projects.AsNoTracking().Where(scope.Visibility(context.ProjectTasks)).SingleOrDefaultAsync(p => p.Id == id, token);
    public async Task<IReadOnlyList<Project>> ListReadableAsync(SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        await context.Projects.AsNoTracking().Where(scope.Visibility(context.ProjectTasks)).ToArrayAsync(token);
    public async Task AddAsync(Project entity, CancellationToken cancellationToken)
    {
        await context.Projects.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) =>
        await context.Projects.AsNoTracking().ToListAsync(cancellationToken);
}
