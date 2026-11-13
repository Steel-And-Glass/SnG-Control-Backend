namespace SnG.Control.Domain.Entities;

public sealed class BudgetQuotationVersion
{
    private BudgetQuotationVersion() { }
    public BudgetQuotationVersion(Guid projectId, int versionNumber, string originalFileName,
        string documentClientName, string documentProjectName, string documentLocation,
        string clientLine, string productionLine, string preparedBy, string documentSgCode)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId cannot be empty.", nameof(projectId));
        if (versionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentSgCode);
        if (!documentSgCode.StartsWith("SG", StringComparison.Ordinal) || documentSgCode.Length < 3
            || documentSgCode.Skip(2).Any(c => c is < '0' or > '9')) throw new ArgumentException("Invalid document SG.");
        Id = Guid.NewGuid(); ProjectId = projectId; VersionNumber = versionNumber;
        OriginalFileName = originalFileName.Replace('\\', '/').Split('/').Last();
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalFileName);
        OriginalFileStorageKey = $"projects/{ProjectId:D}/budget-quotations/{Id:D}/original.xlsx";
        DocumentClientName = documentClientName; DocumentProjectName = documentProjectName; DocumentLocation = documentLocation;
        ClientLine = clientLine; ProductionLine = productionLine; PreparedBy = preparedBy; DocumentSgCode = documentSgCode;
        var now = DateTime.UtcNow;
        ConfirmedAt = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public int VersionNumber { get; private set; }
    public string OriginalFileName { get; private set; } = "";
    public string OriginalFileStorageKey { get; private set; } = "";
    public string? ConfirmedFileStorageKey { get; private set; }
    public string OriginalFileContentType { get; private set; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string DocumentClientName { get; private set; } = "";
    public string DocumentProjectName { get; private set; } = "";
    public string DocumentLocation { get; private set; } = "";
    public string ClientLine { get; private set; } = "";
    public string ProductionLine { get; private set; } = "";
    public string PreparedBy { get; private set; } = "";
    public string DocumentSgCode { get; private set; } = "";
    public DateTime ConfirmedAt { get; private set; }

    public void SetConfirmedFileStorageKey(string key)
    {
        if (ConfirmedFileStorageKey is not null) throw new InvalidOperationException("The operational file is already assigned.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.StartsWith('/') || key.Contains('\\') || key.Contains(':')
            || key.Split('/').Any(segment => segment is "" or "." or ".." || segment.Any(char.IsControl)))
            throw new ArgumentException("The operational file key must be relative and safe.", nameof(key));
        ConfirmedFileStorageKey = key;
    }

    public void ClearConfirmedFileStorageKey()
    {
        if (ConfirmedFileStorageKey is null) throw new InvalidOperationException("The operational file is unavailable.");
        ConfirmedFileStorageKey = null;
    }
}
