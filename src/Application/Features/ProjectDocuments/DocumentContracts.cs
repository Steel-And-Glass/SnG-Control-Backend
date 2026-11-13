using SnG.Control.Domain.Entities;
using SnG.Control.Application.Features.ProjectTasks.SendToBudgets;

namespace SnG.Control.Application.Features.ProjectDocuments;

public sealed class DocumentException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record DocumentUpload(string PartName, string FileName, string? ContentType, Guid? ReplacesDocumentId, Stream Content);
public sealed record ProjectDocumentState(Project Project, IReadOnlyList<ProjectDocument> Documents,
    ProjectTask? PrepareQuotation, ProjectDocumentHandoff? Handoff, IReadOnlyList<ProjectDocumentHandoffItem> HandoffItems, ProjectTask? ReviewQuotation = null, IReadOnlyList<ProjectTask>? HistoricalTasks = null);
public sealed record DocumentPerson(string? Email, string? Name);
public sealed record DocumentResponse(Guid Id, string OriginalFileName, string? ContentType, long SizeBytes, DateTime UploadedAt,
    DocumentPerson UploadedBy, Guid UploadBatchId, bool IsCurrent, DateTime? ReplacedAt, Guid? ReplacedByDocumentId, bool InInitialHandoff)
{
    public static DocumentResponse From(ProjectDocument document, IReadOnlySet<Guid> initial) => new(document.Id, document.OriginalFileName,
        document.ContentType, document.SizeBytes, document.UploadedAt, new(document.UploadedByEmail, document.UploadedByName),
        document.UploadBatchId, document.IsCurrent, document.ReplacedAt, document.ReplacedByDocumentId, initial.Contains(document.Id));
}
public sealed record HandoffResponse(Guid Id, DateTime SentAt, DocumentPerson SentBy, IReadOnlyList<Guid> DocumentIds);
public sealed record DocumentListResponse(IReadOnlyList<DocumentResponse> Documents, HandoffResponse? InitialHandoff,
    bool CanModify, bool CanRemove, bool CanSendToBudgets);
public sealed record DocumentBatchResponse(Guid UploadBatchId, IReadOnlyList<DocumentResponse> Documents);
public sealed record DocumentDownload(Stream Content, string FileName);

public interface IProjectDocumentStore
{
    Task<ProjectDocumentState?> LoadAsync(Guid projectId, CancellationToken token);
    Task<DocumentBatchResponse> UploadAsync(Guid projectId, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token);
    Task RemoveAsync(Guid projectId, Guid documentId, CancellationToken token);
    Task<SendProjectToBudgetsResult> SendToBudgetsAsync(Guid projectId, CancellationToken token);
}

public interface IReceiptDocumentStore
{
    Task RequireReceiptUploadAccessAsync(Guid id, CancellationToken token);
    Task<DocumentBatchResponse> UploadReceiptsAsync(Guid id, IAsyncEnumerable<DocumentUpload> uploads, CancellationToken token);
}
