using Microsoft.EntityFrameworkCore;
using SnG.Control.Application.Features.Clients;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Persistence.Stores;

public class EfClientStore(SnGControlDbContext context) : IClientStore
{
    private IQueryable<Client> Readable(SnG.Control.Application.Access.ProjectReadScope scope)
    {
        var clients = context.Clients.AsNoTracking();
        var projects = context.Projects.Where(scope.Visibility(context.ProjectTasks));
        return scope.AllClients ? clients : clients.Where(c => projects.Any(p => p.ClientId == c.Id));
    }
    public Task<Client?> GetReadableByIdAsync(Guid id, SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        Readable(scope).SingleOrDefaultAsync(c => c.Id == id, token);
    public async Task<IReadOnlyList<Client>> ListReadableAsync(SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        await Readable(scope).ToArrayAsync(token);
    public async Task AddAsync(Client entity, CancellationToken cancellationToken)
    {
        await context.Clients.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Clients.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Client>> ListAsync(CancellationToken cancellationToken) =>
        await context.Clients.AsNoTracking().ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Clients.AsNoTracking().AnyAsync(client => client.Id == id, cancellationToken);
}
