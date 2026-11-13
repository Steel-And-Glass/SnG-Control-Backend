using SnG.Control.Application.Features.Projects;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Tests.TestDoubles;

public class ProjectStoreDouble : IProjectStore
{
    public List<ProjectTask> Tasks { get; set; } = [];
    public async Task<Project?> GetReadableByIdAsync(Guid id, SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token)
    { var p = await GetByIdAsync(id, token); return p is not null && scope.Visibility(Tasks.AsQueryable()).Compile()(p) ? p : null; }
    public async Task<IReadOnlyList<Project>> ListReadableAsync(SnG.Control.Application.Access.ProjectReadScope scope, CancellationToken token) =>
        (await ListAsync(token)).Where(p => scope.Visibility(Tasks.AsQueryable()).Compile()(p)).ToArray();
    public List<Project> Items { get; } = [];
    public List<CancellationToken> ReceivedTokens { get; } = [];

    public Task AddAsync(Project entity, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Items.SingleOrDefault(entity => entity.Id == id));
    }

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
    {
        ReceivedTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Project>>(Items.ToArray());
    }
}
