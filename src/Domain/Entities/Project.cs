using SnG.Control.Domain.Enums;

namespace SnG.Control.Domain.Entities;

public class Project
{
    // EF Core can materialize historical projects whose creator is unknown.
    private Project() { }

    public Project(Guid clientId, string name, string city, ProductLine productLine,
        string createdByGoogleSubject, string? createdByEmail = null, string? createdByName = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("ClientId cannot be empty.", nameof(clientId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByGoogleSubject);

        if (!Enum.IsDefined(productLine))
        {
            throw new ArgumentOutOfRangeException(nameof(productLine), productLine, "ProductLine must be a defined value.");
        }

        Id = Guid.NewGuid();
        ClientId = clientId;
        Name = name.Trim();
        City = city.Trim();
        ProductLine = productLine;
        CreatedByGoogleSubject = createdByGoogleSubject;
        CreatedByEmail = string.IsNullOrWhiteSpace(createdByEmail) ? null : createdByEmail;
        CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? null : createdByName;
        SgCode = null;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string Name { get; private set; } = "";
    public string City { get; private set; } = "";
    public string? CreatedByGoogleSubject { get; private set; }
    public string? CreatedByEmail { get; private set; }
    public string? CreatedByName { get; private set; }
    public ProductLine ProductLine { get; private set; }
    public string? SgCode { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void SetSgCode(string sgCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sgCode);
        var normalized = sgCode.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("SG", StringComparison.Ordinal) || normalized.Length < 3
            || normalized.Skip(2).Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException("SgCode must be SG followed by ASCII digits.", nameof(sgCode));
        }
        if (SgCode == normalized) return;

        SgCode = normalized;
        UpdatedAt = DateTime.UtcNow;
    }
}
