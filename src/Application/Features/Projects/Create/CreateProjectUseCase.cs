using SnG.Control.Application.Features.Clients;
using SnG.Control.Application.Identity;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Projects.Create;

public class CreateProjectUseCase(IProjectStore projects, IClientStore clients, ICurrentUser currentUser)
{
    public async Task<ProjectResponse?> ExecuteAsync(
        Guid clientId, string name, string city, ProductLine productLine, CancellationToken cancellationToken)
    {
        var project = new Project(clientId, name, city, productLine, currentUser.Subject, currentUser.Email, currentUser.Name);
        if (!await clients.ExistsAsync(clientId, cancellationToken))
        {
            return null;
        }

        await projects.AddAsync(project, cancellationToken);
        return ProjectResponse.FromEntity(project, currentUser.Subject);
    }
}
