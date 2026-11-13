using System.Globalization;
using SnG.Control.Domain.Enums;
using SnG.Control.Domain.ValueObjects;
using SnG.Control.Application.Scheduling;

namespace SnG.Control.Application.Features.Management;

// Store rows: one consistent snapshot, aggregated per project in constant query count.
public sealed record ManagementDashboardProjectRow(Guid ProjectId, Guid ClientId, string ProjectName, string ClientName,
    string? SgCode, string City, ProductLine ProductLine, bool HasCurrentQuotation, decimal? QuotedArea, long QuotedItems,
    decimal? QuotedValue, decimal? ReferenceArea, long MeasuredStructures, long PendingStructures);

public sealed record ManagementDashboardSnapshot(DateTime GeneratedAt,
    IReadOnlyList<ManagementDashboardProjectRow> Projects, IReadOnlyList<ManagementTaskData> Tasks);

public interface IManagementDashboardStore
{
    Task<ManagementDashboardSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}

// Public contract: exact decimals travel as strings; no floating-point values for m² or COP.
public sealed record ManagementDashboardProject(Guid ProjectId, Guid ClientId, string ProjectName, string ClientName,
    string? SgCode, string City, string ProductLine, string? QuotedSquareMeters, long QuotedItems, string? QuotedValue,
    string? ReferenceSquareMeters, long MeasuredStructures, long PendingStructures);

public sealed record ManagementDashboardTotals(int ProjectCount, long QuotationCount, long QuotedItems,
    string? QuotedSquareMeters, string QuotedValue, long CompletedOnTime, long CompletedLate);

public sealed record ManagementDashboardDatasetResponse(DateTime GeneratedAt, string CurrencyCode,
    ManagementDashboardTotals Totals, IReadOnlyList<ManagementDashboardProject> Projects,
    IReadOnlyList<ManagementTaskResponse> Tasks);

public sealed class GetManagementDashboardDatasetUseCase(IManagementDashboardStore store, TaskTiming timing)
{
    public async Task<ManagementDashboardDatasetResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var tasks = ManagementTaskRows.Project(snapshot.Tasks, timing);

        var quotedValue = snapshot.Projects.Where(p => p.HasCurrentQuotation).Sum(p => p.QuotedValue ?? 0m);
        // An exact sum can exceed the public numeric(20,2) contract. Never clamp or wrap it.
        if (quotedValue > Money.Maximum)
            throw new InvalidOperationException("Dashboard quoted value exceeds the monetary contract.");

        var quotedArea = snapshot.Projects.Where(p => p.QuotedArea is not null).Select(p => p.QuotedArea!.Value).ToArray();
        var projects = snapshot.Projects.Select(p => new ManagementDashboardProject(p.ProjectId, p.ClientId, p.ProjectName,
            p.ClientName, p.SgCode, p.City, p.ProductLine.ToString(), Exact(p.QuotedArea), p.QuotedItems,
            MoneyText(p.QuotedValue), Exact(p.ReferenceArea), p.MeasuredStructures, p.PendingStructures)).ToArray();

        var totals = new ManagementDashboardTotals(snapshot.Projects.Count,
            snapshot.Projects.LongCount(p => p.HasCurrentQuotation), snapshot.Projects.Sum(p => p.QuotedItems),
            quotedArea.Length == 0 ? null : SurfaceDefinition.Exact(quotedArea.Sum()),
            MoneyText(quotedValue)!, tasks.LongCount(t => t.LeadTimeStatus == LeadTimeStatus.CompletedOnTime),
            tasks.LongCount(t => t.LeadTimeStatus == LeadTimeStatus.CompletedLate));

        return new(snapshot.GeneratedAt, Money.CurrencyCode, totals, projects, tasks);
    }

    private static string? Exact(decimal? value) => value is null ? null : SurfaceDefinition.Exact(value.Value);

    private static string? MoneyText(decimal? value) =>
        value is null ? null : Money.Normalize(value.Value).ToString("0.00", CultureInfo.InvariantCulture);
}
