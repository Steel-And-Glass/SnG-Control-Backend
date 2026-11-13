using SnG.Control.Application.Scheduling;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Application.Features.Management;

public sealed record ManagementTaskData(ProjectTask Task, string ProjectName, Guid ClientId,
    string ClientName, string? SgCode, string City, ProductLine ProductLine);

public interface IManagementReadStore
{
    Task<QuotationPortfolioData> GetQuotationPortfolioAsync(CancellationToken cancellationToken);
    Task<int> CountProjectsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementTaskData>> ListTasksAsync(CancellationToken cancellationToken);
}

public sealed record ManagementTaskResponse(Guid TaskId, Guid ProjectId, string ProjectName,
    Guid ClientId, string ClientName, string? SgCode, string City, ProductLine ProductLine,
    OperationalArea Area, ProjectTaskType Type, ProjectTaskStatus Status, DateTime AssignedAt,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime? DeadlineAt, bool? WasOverdue, LeadTimeStatus LeadTimeStatus, int? QuotationRound = 1);

public sealed record ManagementTaskCounts(int Pending, int InProgress, int Completed);
public sealed record ManagementLeadTimeCounts(int NotStarted, int OnTrack, int DueSoon, int DueToday,
    int Overdue, int CompletedOnTime, int CompletedLate, int UntrackedLegacy);
public sealed record ManagementAreaSummary(OperationalArea Area, int Pending, int InProgress,
    int Completed, int Overdue, int DueToday, int DueSoon);
public sealed record ManagementAttentionItem(Guid TaskId, Guid ProjectId, string ProjectName,
    string ClientName, string? SgCode, OperationalArea Area, ProjectTaskType TaskType,
    ProjectTaskStatus TaskStatus, LeadTimeStatus LeadTimeStatus, DateTime? DeadlineAt, DateTime? StartedAt);
public sealed record ManagementAttention(int Total, IReadOnlyList<ManagementAttentionItem> Items);
public sealed record ManagementOverviewResponse(int TotalProjects, ManagementTaskCounts Tasks,
    ManagementLeadTimeCounts LeadTime, IReadOnlyList<ManagementAreaSummary> Areas, ManagementAttention Attention);
