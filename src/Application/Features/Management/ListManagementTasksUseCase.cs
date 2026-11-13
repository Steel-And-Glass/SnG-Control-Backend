using SnG.Control.Application.Scheduling;

namespace SnG.Control.Application.Features.Management;

public sealed class ListManagementTasksUseCase(IManagementReadStore store, TaskTiming timing)
{
    public async Task<IReadOnlyList<ManagementTaskResponse>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var data = await store.ListTasksAsync(cancellationToken);
        return ManagementTaskRows.Project(data, timing);
    }
}
