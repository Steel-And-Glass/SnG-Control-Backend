using System.Text.Json;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Domain.Entities;

public sealed class MeasurementChecklist
{
    private MeasurementChecklist() { }
    public MeasurementChecklist(Guid projectId, Guid quotationId, int round, Guid taskId, DateTime at, string subject, bool isIncremental = false)
    {
        if (projectId == Guid.Empty || quotationId == Guid.Empty || taskId == Guid.Empty || round < 1) throw new ArgumentException("Invalid checklist source.");
        ValidateActor(at, subject);
        Id = Guid.NewGuid(); ProjectId = projectId; CommercialQuotationId = quotationId; QuotationRound = round; TaskId = taskId;
        CreatedAt = at; CreatedBySubject = subject; IsIncremental = isIncremental;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CommercialQuotationId { get; private set; }
    public int QuotationRound { get; private set; }
    public Guid TaskId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public bool IsIncremental { get; private set; }
    public long Revision { get; private set; }
    public Guid? SubmissionId { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public string? SubmittedBySubject { get; private set; }
    public string? SnapshotJson { get; private set; }
    public void Advance()
    {
        if (SubmissionId is not null) throw new InvalidOperationException("Measurements already submitted.");
        Revision = checked(Revision + 1);
    }
    public void Submit(DateTime at, string subject, string snapshot, Guid? submissionId = null)
    {
        if (IsIncremental) throw new InvalidOperationException("Incremental workspaces cannot be submitted globally.");
        ValidateActor(at, subject);
        if (at < CreatedAt || string.IsNullOrWhiteSpace(snapshot)) throw new ArgumentException("Invalid submission.");
        Advance(); SubmissionId = submissionId ?? Guid.NewGuid(); SubmittedAt = at; SubmittedBySubject = subject; SnapshotJson = snapshot;
    }
    internal static void ValidateActor(DateTime at, string subject)
    {
        if (at.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
    }
}

public sealed class MeasurementStructure
{
    private MeasurementStructure() { }
    private MeasurementStructure(MeasurementChecklist checklist, DateTime at, string subject)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        if (checklist.SubmissionId is not null) throw new InvalidOperationException("Measurements already submitted.");
        Id = Guid.NewGuid(); ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; CreatedAt = at; CreatedBySubject = subject;
    }
    public static MeasurementStructure Quoted(MeasurementChecklist checklist, CommercialQuotationItem item, int ordinal, DateTime at, string subject)
    {
        if (item.CommercialQuotationId != checklist.CommercialQuotationId || ordinal < 1 || ordinal > item.Quantity) throw new ArgumentException("Invalid quotation unit.");
        return new(checklist, at, subject) { CommercialQuotationItemId = item.Id, UnitOrdinal = ordinal, UnitCount = item.Quantity,
            ItemNumber = item.ItemNumber, Description = item.System, Location = item.Location, Width = item.Width, Height = item.Height, Note = item.Observations };
    }
    public static MeasurementStructure Added(MeasurementChecklist checklist, string description, decimal width, decimal height, string? note, DateTime at, string subject)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 2000 || width <= 0 || height <= 0 || note?.Length > 4000) throw new ArgumentException("Invalid onsite structure.");
        return new(checklist, at, subject) { Description = description.Trim(), Width = width, Height = height, Note = note, UnitOrdinal = 1, UnitCount = 1 };
    }
    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CommercialQuotationItemId { get; private set; }
    public int UnitOrdinal { get; private set; }
    public int UnitCount { get; private set; }
    public string? ItemNumber { get; private set; }
    public string Description { get; private set; } = "";
    public string? Location { get; private set; }
    public decimal Width { get; private set; }
    public decimal Height { get; private set; }
    public string? TechnicalJson { get; private set; }
    public InstallationTechnicalDetails? Technical => TechnicalJson is null ? null : JsonSerializer.Deserialize<InstallationTechnicalDetails>(TechnicalJson);
    public Guid? SketchVersionId { get; private set; }
    public void RecordTechnical(InstallationTechnicalDetails details, DateTime at, string subject)
    {
        ValidateChange(at, subject); details.Validate(ActualHeight);
        if (MeasuredAt != null && !details.IsFullyComplete) throw new InvalidOperationException("Unmark before saving incomplete details.");
        TechnicalJson = JsonSerializer.Serialize(details);
    }
    public void SetSketch(Guid versionId, DateTime at, string subject)
    {
        ValidateChange(at, subject);
        if (versionId == Guid.Empty) throw new ArgumentException("Sketch version required.");
        SketchVersionId = versionId;
    }
    public bool IsTechnicallyComplete => AssessmentComplete && MeasurementType != null && !string.IsNullOrWhiteSpace(ActualLocation) && ActualWidth > 0 && ActualHeight > 0 && !string.IsNullOrWhiteSpace(ActualSystemText) && Technical?.IsFullyComplete == true;
    public decimal? ActualWidth { get; private set; }
    public decimal? ActualHeight { get; private set; }
    public string? ActualSystemText { get; private set; }
    public void RecordActual(decimal width, decimal height, string systemText, DateTime at, string subject)
    {
        ValidateChange(at, subject);
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(systemText) || systemText.Length > 2000) throw new ArgumentException("Invalid actual measurements.");
        Technical?.Validate(height);
        ActualWidth = width; ActualHeight = height; ActualSystemText = systemText.Trim();
    }
    public string? ComparisonJson { get; private set; }
    public string? ModificationJson { get; private set; }
    public InstallationComparison? Comparison => ComparisonJson == null ? null : JsonSerializer.Deserialize<InstallationComparison>(ComparisonJson);
    public InstallationModification? Modification => ModificationJson == null ? null : JsonSerializer.Deserialize<InstallationModification>(ModificationJson);
    public bool ComparisonCurrent => Comparison is { } c && c.MeasurementType == MeasurementType && c.Width == ActualWidth && c.Height == ActualHeight;
    public decimal? WidthDifferenceMm => ComparisonCurrent && Comparison?.Resolution == "Comparable" ? Math.Abs((ActualWidth!.Value - Width) * 1000m) : null;
    public decimal? HeightDifferenceMm => ComparisonCurrent && Comparison?.Resolution == "Comparable" ? Math.Abs((ActualHeight!.Value - Height) * 1000m) : null;
    public string ComparisonStatus => !ComparisonCurrent || Comparison?.Resolution != "Comparable" ? "PendingReview" : WidthDifferenceMm > 100m || HeightDifferenceMm > 100m ? "OverThreshold" : "NoDimensionalChange";
    private string ModificationEvidence => JsonSerializer.Serialize(new { ActualWidth, ActualHeight, ActualSystemText, MeasurementType, ActualLocation, TechnicalJson });
    public bool ModificationComplete => Modification is { } m && !string.IsNullOrWhiteSpace(m.Reason) && !string.IsNullOrWhiteSpace(m.Description) && m.SketchVersionId != null && m.Evidence == ModificationEvidence;
    public bool AssessmentComplete => ComparisonCurrent && (ComparisonStatus != "OverThreshold" || Modification != null) && (Modification == null || ModificationComplete);
    public void ResolveComparison(string resolution, string reason, DateTime at, string subject)
    {
        ValidateChange(at, subject);
        if (resolution is not ("Comparable" or "NotComparable") || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || MeasurementType == null || ActualWidth == null || ActualHeight == null || (resolution == "Comparable" && CommercialQuotationItemId == null)) throw new ArgumentException("Invalid comparison resolution.");
        if (MeasuredAt != null) throw new InvalidOperationException("Unmark before reviewing comparison.");
        ComparisonJson = JsonSerializer.Serialize(new InstallationComparison(resolution, reason.Trim(), MeasurementType, ActualWidth, ActualHeight));
    }
    public void RecordModification(string type, string? reason, string? description, Guid? sketchId, DateTime at, string subject)
    {
        ValidateChange(at, subject);
        if (type is not ("Dimensional" or "System" or "Typology" or "Configuration" or "Structural" or "Other") || reason?.Length > 2000 || description?.Length > 4000 || (sketchId != null && sketchId != SketchVersionId)) throw new ArgumentException("Invalid modification.");
        if (MeasuredAt != null) throw new InvalidOperationException("Unmark before recording modification.");
        ModificationJson = JsonSerializer.Serialize(new InstallationModification(Guid.NewGuid(), type, reason?.Trim(), description?.Trim(), sketchId, ModificationEvidence));
    }
    public string? MeasurementType { get; private set; }
    public string? ActualLocation { get; private set; }
    public string? InstallationObservations { get; private set; }
    public void RecordSiteDetails(string? measurementType, string? location, string? observations, DateTime at, string subject)
    {
        ValidateChange(at, subject);
        if (measurementType is not (null or "Agreed" or "Production" or "Opening") || location?.Length > 500 || observations?.Length > 4000) throw new ArgumentException("Invalid site details.");
        if (MeasuredAt != null && (measurementType == null || string.IsNullOrWhiteSpace(location))) throw new InvalidOperationException("Unmark before saving incomplete details.");
        MeasurementType = measurementType; ActualLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        InstallationObservations = string.IsNullOrWhiteSpace(observations) ? null : observations.Trim();
    }
    public void Reinstate(DateTime at, string subject)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        if (RetiredAt is null || at < RetiredAt) throw new InvalidOperationException("Structure is not retired.");
        RetiredAt = null; RetiredBySubject = null; MeasuredAt = null; MeasuredBySubject = null;
    }
    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public DateTime? MeasuredAt { get; private set; }
    public string? MeasuredBySubject { get; private set; }
    public DateTime? RetiredAt { get; private set; }
    public string? RetiredBySubject { get; private set; }
    public void Measure(bool measured, DateTime at, string subject)
    {
        ValidateChange(at, subject); MeasuredAt = measured ? at : null; MeasuredBySubject = measured ? subject : null;
    }
    public void Retire(DateTime at, string subject)
    {
        ValidateChange(at, subject); RetiredAt = at; RetiredBySubject = subject;
    }
    private void ValidateChange(DateTime at, string subject)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        if (RetiredAt is not null || at < CreatedAt || at < MeasuredAt) throw new InvalidOperationException("Invalid structure transition.");
    }
}

public sealed class MeasurementEvent
{
    private MeasurementEvent() { }
    public MeasurementEvent(MeasurementChecklist checklist, Guid requestKey, string fingerprint, string action, Guid? structureId, DateTime at, string subject, string? email, string? name, string? detailsJson = null, string? technicalChangeJson = null, string? sketchChangeJson = null, string? assessmentChangeJson = null)
    {
        MeasurementChecklist.ValidateActor(at, subject);
        if (requestKey == Guid.Empty) throw new ArgumentException("Request key required.");
        Id = Guid.NewGuid(); ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; RequestKey = requestKey; Fingerprint = fingerprint;
        AssessmentChangeJson = assessmentChangeJson; TechnicalChangeJson = technicalChangeJson; SketchChangeJson = sketchChangeJson; DetailsJson = detailsJson; Action = action; StructureId = structureId; At = at; ActorSubject = subject; ActorEmail = email; ActorName = name; Revision = checklist.Revision;
    }
    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string? AssessmentChangeJson { get; private set; }
    public string? TechnicalChangeJson { get; private set; }
    public string? SketchChangeJson { get; private set; }
    public string? DetailsJson { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Action { get; private set; } = "";
    public Guid? StructureId { get; private set; }
    public long Revision { get; private set; }
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string? ActorEmail { get; private set; }
    public string? ActorName { get; private set; }
}

public sealed class MeasurementSubmissionDocument
{
    private MeasurementSubmissionDocument() { }
    public MeasurementSubmissionDocument(MeasurementChecklist checklist, ProjectDocument document)
    {
        if (checklist.SubmissionId is null || document.ProjectId != checklist.ProjectId || !document.IsMeasurementDocument || !document.IsCurrent) throw new ArgumentException("Invalid measurement document.");
        ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; ProjectDocumentId = document.Id;
    }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ProjectDocumentId { get; private set; }
}
