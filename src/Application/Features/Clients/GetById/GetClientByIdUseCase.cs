using SnG.Control.Application.Access;
namespace SnG.Control.Application.Features.Clients.GetById;

public class GetClientByIdUseCase(IClientStore store, ProjectReadAccess access)
{
    public async Task<ClientResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await store.GetReadableByIdAsync(id, access.Scope, cancellationToken);
        return entity is null ? null : ClientResponse.FromEntity(entity);
    }
}
