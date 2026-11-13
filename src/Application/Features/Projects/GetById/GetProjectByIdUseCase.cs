using SnG.Control.Application.Access;
namespace SnG.Control.Application.Features.Projects.GetById;

public class GetProjectByIdUseCase(IProjectStore store, ProjectReadAccess access)
{
    public async Task<ProjectResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await store.GetReadableByIdAsync(id, access.Scope, cancellationToken);
        return entity is null ? null : ProjectResponse.FromEntity(entity, access.Scope.Subject);
    }
}
