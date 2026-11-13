namespace SnG.Control.Domain.Entities;

public sealed class ProjectDocument
{
    public const long MaxFileBytes = 100_000_000;
    public const long MaxBatchBytes = 300_000_000;
    public const int MaxCurrentDocuments = 15;
    public const int MaxFileNameLength = 255;

    private ProjectDocument() { }
    public ProjectDocument(Guid id, Guid projectId, string fileName, string? contentType, long sizeBytes,
        Guid batchId, DateTime uploadedAt, string subject, string? email, string? name, bool isPaymentReceipt = false, bool isMeasurementDocument = false, bool isEngineeringDocument = false)
    {
        if (id == Guid.Empty || projectId == Guid.Empty || batchId == Guid.Empty) throw new ArgumentException("Document identifiers required.");
        ValidateFileName(fileName);
        if (sizeBytes <= 0 || sizeBytes > MaxFileBytes) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        if (uploadedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Id = id; ProjectId = projectId; OriginalFileName = fileName;
        ContentType = contentType is { Length: > 0 and <= 255 } && !contentType.Any(char.IsControl) ? contentType : null;
        SizeBytes = sizeBytes; UploadBatchId = batchId; UploadedAt = uploadedAt;
        UploadedByGoogleSubject = subject; UploadedByEmail = email; UploadedByName = name;
        if ((isPaymentReceipt ? 1 : 0) + (isMeasurementDocument ? 1 : 0) + (isEngineeringDocument ? 1 : 0) > 1) throw new ArgumentException("Document categories are exclusive.");
        IsEngineeringDocument = isEngineeringDocument;
        StorageKey = Key(projectId, id); IsPaymentReceipt = isPaymentReceipt; IsMeasurementDocument = isMeasurementDocument;
    }
    public static string Key(Guid projectId, Guid id) => $"projects/{projectId:D}/documents/{id:D}/content";
    public static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > MaxFileNameLength || fileName is "." or ".."
            || fileName.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' || c is '<' or '>' or '"' or '|' or '?' or '*')
            || fileName.EndsWith('.') || fileName.EndsWith(' ')) throw new ArgumentException("Invalid document filename.");
    }
    public void ReplaceWith(ProjectDocument replacement, DateTime at)
    {
        if (!IsCurrent || replacement.Id == Id || replacement.ProjectId != ProjectId || replacement.IsPaymentReceipt != IsPaymentReceipt || replacement.IsEngineeringDocument != IsEngineeringDocument || replacement.IsMeasurementDocument != IsMeasurementDocument || !replacement.IsCurrent)
            throw new InvalidOperationException("Invalid document replacement.");
        ValidateChangeTime(at);
        ReplacedAt = at; ReplacedByDocumentId = replacement.Id;
    }
    public void Remove(DateTime at)
    {
        if (!IsCurrent) throw new InvalidOperationException("Only current documents can be removed.");
        ValidateChangeTime(at); RemovedAt = at;
    }
    private void ValidateChangeTime(DateTime at)
    {
        if (at.Kind != DateTimeKind.Utc || at < UploadedAt) throw new ArgumentException("Invalid document timestamp.");
    }
    public bool IsEngineeringDocument { get; private set; }
    public bool IsMeasurementDocument { get; private set; }
    public bool IsPaymentReceipt { get; private set; }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string OriginalFileName { get; private set; } = "";
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = "";
    public DateTime UploadedAt { get; private set; }
    public string UploadedByGoogleSubject { get; private set; } = "";
    public string? UploadedByEmail { get; private set; }
    public string? UploadedByName { get; private set; }
    public Guid UploadBatchId { get; private set; }
    public DateTime? ReplacedAt { get; private set; }
    public Guid? ReplacedByDocumentId { get; private set; }
    public DateTime? RemovedAt { get; private set; }
    public bool IsCurrent => ReplacedAt is null && RemovedAt is null;
}
