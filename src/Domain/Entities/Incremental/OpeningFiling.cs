namespace SnG.Control.Domain.Entities.Incremental;

/// <summary>Rules shared by filing drafts and their immutable release manifests.</summary>
public static class OpeningFilingRules
{
    public static readonly IReadOnlyList<string> Categories = Array.AsReadOnly(new[]
    {
        "Structures", "Sections", "Plans", "UnoptimizedCutList", "Glass", "Materials", "GlassModulation", "Additional"
    });
    public static string NormalizeIdentifier(string original)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > 80 ||
            original.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')))
            throw new ArgumentException("A bounded ASCII identifier is required.");
        return original.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }
    public static void Category(string category)
    {
        if (!Categories.Contains(category, StringComparer.Ordinal)) throw new ArgumentException("Invalid filing category.");
    }
}

public sealed class OpeningFilingWorkspace
{
    private OpeningFilingWorkspace() { }
    public OpeningFilingWorkspace(OpeningFilingDelivery delivery, string subject, DateTime at)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        FilingId = delivery.Id; ProjectId = delivery.ProjectId; ResponsibleSubject = subject; CreatedAt = at;
    }
    public Guid FilingId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ResponsibleSubject { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public long Revision { get; private set; }
    public string? NumberOriginal { get; private set; }
    public string? NumberNormalized { get; private set; }
    public DateTime? FirstConfirmedAt { get; private set; }
    public void Advance() => Revision = checked(Revision + 1);
    public void SetNumber(string original)
    {
        if (FirstConfirmedAt != null) throw new InvalidOperationException("Confirmed filing number is immutable.");
        NumberNormalized = OpeningFilingRules.NormalizeIdentifier(original); NumberOriginal = original; Advance();
    }
    public void Confirm(DateTime at)
    {
        if (at.Kind != DateTimeKind.Utc || NumberNormalized == null) throw new InvalidOperationException("Number and UTC required.");
        FirstConfirmedAt ??= at; Advance();
    }
}
public sealed class OpeningFilingVersion
{
    private OpeningFilingVersion() { }
    public OpeningFilingVersion(OpeningFilingWorkspace filing, int number, string? reason, DateTime at, string actor)
    {
        MeasurementChecklist.ValidateActor(at, actor);
        if (number < 1 || (number > 1 && (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)))
            throw new ArgumentException("Amendment requires a reason.");
        Id = Guid.NewGuid(); FilingId = filing.FilingId; ProjectId = filing.ProjectId;
        Number = number; Reason = reason; CreatedAt = at; CreatedBySubject = actor;
    }
    public Guid Id { get; private set; }
    public Guid FilingId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Number { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public DateTime? ConfirmedAt { get; private set; }
    public string? ConfirmedBySubject { get; private set; }
    public string? ManifestCanonical { get; private set; }
    public string? ManifestSha256 { get; private set; }
    public void RequireDraft() { if (ConfirmedAt != null) throw new InvalidOperationException("Version confirmed."); }
    public void Confirm(DateTime at, string actor, string manifest)
    {
        RequireDraft(); MeasurementChecklist.ValidateActor(at, actor); IncrementalEvidence.Json(manifest);
        ConfirmedAt = at; ConfirmedBySubject = actor; ManifestCanonical = manifest; ManifestSha256 = IncrementalEvidence.Hash(manifest);
    }
}
public sealed class OpeningFilingMember
{
    private OpeningFilingMember() { }
    public OpeningFilingMember(OpeningFilingVersion version, Guid structureId, string original, bool confirmed)
    {
        version.RequireDraft(); if (structureId == Guid.Empty) throw new ArgumentException("Structure required.");
        VersionId = version.Id; ProjectId = version.ProjectId; StructureId = structureId;
        OpNormalized = OpeningFilingRules.NormalizeIdentifier(original); OpOriginal = original; Confirmed = confirmed;
    }
    public Guid VersionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
    public string OpOriginal { get; private set; } = "";
    public string OpNormalized { get; private set; } = "";
    public bool Confirmed { get; private set; }
}
public sealed class OpeningFilingDocument
{
    private OpeningFilingDocument() { }
    public OpeningFilingDocument(OpeningFilingVersion version, Guid documentId, string category, string hash, Guid lineageId, int fileVersion)
    {
        version.RequireDraft(); OpeningFilingRules.Category(category); IncrementalEvidence.HashValue(hash);
        if (documentId == Guid.Empty || lineageId == Guid.Empty || fileVersion < 1) throw new ArgumentException("Invalid document version.");
        VersionId = version.Id; ProjectId = version.ProjectId; DocumentId = documentId;
        Category = category; Sha256 = hash; LineageId = lineageId; FileVersion = fileVersion;
    }
    public Guid VersionId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid LineageId { get; private set; }
    public int FileVersion { get; private set; }
    public string Category { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public string State { get; private set; } = "Current";
    public void Retire(OpeningFilingVersion version, bool replaced)
    {
        version.RequireDraft();
        if (version.Id != VersionId || State != "Current") throw new InvalidOperationException("Invalid document state.");
        State = replaced ? "Replaced" : "Withdrawn";
    }
}
public sealed class OpeningFilingEvent
{
    private OpeningFilingEvent() { }
    public OpeningFilingEvent(Guid projectId, Guid filingId, Guid key, string fingerprint, string action, DateTime at, string actor, string canonical)
    {
        MeasurementChecklist.ValidateActor(at, actor); IncrementalEvidence.HashValue(fingerprint); IncrementalEvidence.Json(canonical);
        ProjectId = projectId; FilingId = filingId; RequestKey = key; Fingerprint = fingerprint;
        Action = action; At = at; ActorSubject = actor; Canonical = canonical;
    }
    public Guid ProjectId { get; private set; }
    public Guid FilingId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Action { get; private set; } = "";
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string Canonical { get; private set; } = "";
}
/// <summary>One immutable release per confirmed version, for both Production and Logistics. No external dispatch.</summary>
public sealed class OpeningFilingRelease
{
    private OpeningFilingRelease() { }
    public OpeningFilingRelease(OpeningFilingVersion version)
    {
        if (version.ConfirmedAt == null) throw new InvalidOperationException("Confirmed version required.");
        VersionId = version.Id; FilingId = version.FilingId; ProjectId = version.ProjectId;
        At = version.ConfirmedAt.Value; Canonical = version.ManifestCanonical!; Sha256 = version.ManifestSha256!;
    }
    public Guid VersionId { get; private set; }
    public Guid FilingId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateTime At { get; private set; }
    public string Canonical { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
}
