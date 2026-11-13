using SnG.Control.Application.Features.Management;

namespace SnG.Control.Tests.TestDoubles;

public sealed class ManagementReadStoreDouble(ProjectStoreDouble projects, ClientStoreDouble clients, ProjectTaskStoreDouble tasks) : IManagementReadStore
{
    public QuotationPortfolioData Portfolio { get; set; } = new(new(0, 0.00m), new(0, 0), [], [], [],
        new(EmptyMix<QuotationMixEntry>(), EmptyMix<QuotationMixEntry>(), EmptyMix<QuotationMixEntry>(), EmptyMix<QuotationCombinationEntry>()));
    private static QuotationMixDimension<T> EmptyMix<T>() => new(0, 0, 0, new(0, 0, false, []), new(0, 0, false, []), new(0, 0, false, []));
    public int PortfolioCalls { get; private set; }
    public Task<QuotationPortfolioData> GetQuotationPortfolioAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Tokens.Add(token); PortfolioCalls++;
        return Task.FromResult(Portfolio);
    }
    public int CountCalls { get; private set; }
    public int ListCalls { get; private set; }
    public List<CancellationToken> Tokens { get; } = [];
    public Task<int> CountProjectsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Tokens.Add(token); CountCalls++;
        return Task.FromResult(projects.Items.Count);
    }
    public Task<IReadOnlyList<ManagementTaskData>> ListTasksAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Tokens.Add(token); ListCalls++;
        return Task.FromResult<IReadOnlyList<ManagementTaskData>>((from task in tasks.Items
                                                                   join project in projects.Items on task.ProjectId equals project.Id
                                                                   join client in clients.Items on project.ClientId equals client.Id
                                                                   select new ManagementTaskData(task, project.Name, client.Id, client.Name, project.SgCode, project.City, project.ProductLine)).ToArray());
    }
}
