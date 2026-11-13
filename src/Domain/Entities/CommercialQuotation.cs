using SnG.Control.Domain.ValueObjects;

namespace SnG.Control.Domain.Entities;

public sealed class CommercialQuotation
{
    private CommercialQuotation() { }
    public CommercialQuotation(Guid projectId, Guid budgetQuotationVersionId, Guid commercialCatalogId,
        string originalFileName, decimal projectTotal, string confirmedByGoogleSubject, string? confirmedByEmail, string? confirmedByName)
    {
        if (projectId == Guid.Empty || budgetQuotationVersionId == Guid.Empty || commercialCatalogId == Guid.Empty)
            throw new ArgumentException("Quotation references cannot be empty.");
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedByGoogleSubject);
        if (projectTotal < 0) throw new ArgumentOutOfRangeException(nameof(projectTotal));
        Id = Guid.NewGuid(); ProjectId = projectId; BudgetQuotationVersionId = budgetQuotationVersionId; CommercialCatalogId = commercialCatalogId;
        OriginalFileName = originalFileName.Replace('\\', '/').Split('/').Last();
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalFileName);
        FileStorageKey = $"projects/{ProjectId:D}/commercial-quotations/{Id:D}/commercial.xlsx";
        ProjectTotal = Money.Normalize(projectTotal); ConfirmedByGoogleSubject = confirmedByGoogleSubject;
        ConfirmedByEmail = string.IsNullOrWhiteSpace(confirmedByEmail) ? null : confirmedByEmail;
        ConfirmedByName = string.IsNullOrWhiteSpace(confirmedByName) ? null : confirmedByName;
        var now = DateTime.UtcNow; ConfirmedAt = new(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid BudgetQuotationVersionId { get; private set; }
    public Guid CommercialCatalogId { get; private set; }
    public string OriginalFileName { get; private set; } = "";
    public string FileStorageKey { get; private set; } = "";
    public decimal ProjectTotal { get; private set; }
    public DateTime ConfirmedAt { get; private set; }
    public string ConfirmedByGoogleSubject { get; private set; } = "";
    public string? ConfirmedByEmail { get; private set; }
    public string? ConfirmedByName { get; private set; }
}
