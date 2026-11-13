using SnG.Control.Application.Features.Clients;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Tests.TestDoubles;

public class ClientStoreDouble : IClientStore
{
    public ProjectStoreDouble Projects { get; set; } = new();
    private bool CanRead(Guid id, SnG.Control.Application.Access.ProjectReadScope scope) => scope.AllClients || Projects.Items.Any(p => p.ClientId == id && scope.Visibility(Projects.Tasks.AsQueryable()).Compile()(p));
    public async Task<Client?> GetReadableByIdAsync(Guid id, SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token)
    { var c = await GetByIdAsync(id, token); return c is not null && CanRead(id, scope) ? c : null; }
    public async Task<IReadOnlyList<Client>> ListReadableAsync(SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        (await ListAsync(token)).Where(c => CanRead(c.Id, scope)).ToArray();
    public List<Client> Items { get; } = [];
    public List<CancellationToken> ReceivedTokens { get; } = [];
    public bool FailReads { get; set; }

    public Task AddAsync(Client entity, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads) throw new InvalidOperationException("Internal SQL or connection detail that must never reach HTTP.");
        return Task.FromResult(Items.SingleOrDefault(entity => entity.Id == id));
    }

    public Task<IReadOnlyList<Client>> ListAsync(CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Client>>(Items.ToArray());
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Items.Any(entity => entity.Id == id));
    }
}
