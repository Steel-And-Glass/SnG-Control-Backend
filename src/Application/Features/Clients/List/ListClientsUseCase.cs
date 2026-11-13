using SnG.Control.Application.Access;
namespace SnG.Control.Application.Features.Clients.List;

public class ListClientsUseCase(IClientStore store, ProjectReadAccess access)
{
    public async Task<IReadOnlyList<ClientResponse>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var entities = await store.ListReadableAsync(access.Scope, cancellationToken);
        return entities.Select(ClientResponse.FromEntity).ToArray();
    }
}
