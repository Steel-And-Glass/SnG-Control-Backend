using System.Text.Json.Serialization;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Domain.Entities.Production;
// Extraction states only; there is no approval/publication transition.
public enum PreparationState { PendingExtraction = 1, Extracted = 2, Blocked = 3, ReadyForReview = 4 }
[JsonConverter(typeof(JsonStringEnumConverter<ComponentMaterial>))]
public enum ComponentMaterial { Glass = 1, AluminumProfile = 2, MicroperforatedSheet = 3 }
[JsonConverter(typeof(JsonStringEnumConverter<FabricationRoute>))]
public enum FabricationRoute { [JsonStringEnumMemberName("GLASS")] Glass = 1, [JsonStringEnumMemberName("ALUMINUM")] Aluminum = 2 }
public sealed class ProductionPreparation
{
    private ProductionPreparation() { }
    public ProductionPreparation(OpeningFilingRelease release, DateTime at, string actor)
    {
        MeasurementChecklist.ValidateActor(at, actor);
        if (release.VersionId == Guid.Empty || IncrementalEvidence.Hash(release.Canonical) != release.Sha256)
            throw new ArgumentException("Invalid release evidence.");
        Id = Guid.NewGuid(); ProjectId = release.ProjectId; FilingId = release.FilingId;
        ReleaseVersionId = release.VersionId; ManifestSha256 = release.Sha256; CreatedAt = at; CreatedBySubject = actor;
    }
    public void RecordExtraction(PreparationState state,string version)
    {
        if(state is not (PreparationState.PendingExtraction or PreparationState.Extracted or PreparationState.Blocked or PreparationState.ReadyForReview))throw new ArgumentException("Unsupported state.");
        if(!((State==PreparationState.PendingExtraction && state is PreparationState.Extracted or PreparationState.Blocked or PreparationState.ReadyForReview)
            || (State==PreparationState.Blocked && state==PreparationState.PendingExtraction)))throw new InvalidOperationException("Unsupported extraction transition.");
        State=state;ExtractorVersion=version;Revision++;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid FilingId { get; private set; }
    public Guid ReleaseVersionId { get; private set; }
    public string ManifestSha256 { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public PreparationState State { get; private set; } = PreparationState.PendingExtraction;
    public long Revision { get; private set; }
    public string? ExtractorVersion { get; private set; }
    public string? ReviewedBySubject { get; private set; }
}
// Frozen source scope, copied ONLY from the validated release, never current project documents.
public sealed class PreparationSourceMember
{
    private PreparationSourceMember() { }
    public PreparationSourceMember(ProductionPreparation p, OpeningFilingMember m)
    {
        if (p.ReleaseVersionId != m.VersionId || p.ProjectId != m.ProjectId || !m.Confirmed) throw new ArgumentException("Foreign member.");
        PreparationId = p.Id; ReleaseVersionId = p.ReleaseVersionId; StructureId = m.StructureId; Op = m.OpNormalized;
    }
    public Guid PreparationId { get; private set; }
    public Guid ReleaseVersionId { get; private set; }
    public Guid StructureId { get; private set; }
    public string Op { get; private set; } = "";
}
public sealed class PreparationSourceDocument
{
    private PreparationSourceDocument() { }
    public PreparationSourceDocument(ProductionPreparation p, OpeningFilingDocument d)
    {
        if (p.ReleaseVersionId != d.VersionId || p.ProjectId != d.ProjectId || d.State != "Current") throw new ArgumentException("Foreign document.");
        PreparationId = p.Id; ReleaseVersionId = p.ReleaseVersionId; DocumentId = d.DocumentId; Sha256 = d.Sha256; Category = d.Category;
    }
    public Guid PreparationId { get; private set; }
    public Guid ReleaseVersionId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string Category { get; private set; } = "";
}
// Schema foundation, not a published production revision. No Approved state/publication method.
public sealed class ProductionComponentRevision
{
    private ProductionComponentRevision() { }
    public ProductionComponentRevision(ProductionPreparation preparation, int number, DateTime at, string actor)
    {
        MeasurementChecklist.ValidateActor(at, actor); if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        Id = Guid.NewGuid(); PreparationId = preparation.Id; Number = number; CreatedAt = at; CreatedBySubject = actor;
    }
    public Guid Id { get; private set; }
    public void RecordExtraction(PreparationState state)
    { if(state is not (PreparationState.Extracted or PreparationState.Blocked or PreparationState.ReadyForReview))throw new ArgumentException("Unsupported state.");State=state;Revision++; }
    public Guid PreparationId { get; private set; }
    public int Number { get; private set; }
    public long Revision { get; private set; }
    public PreparationState State { get; private set; } = PreparationState.PendingExtraction;
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public string? ReviewedBySubject { get; private set; }
}
public sealed record CandidateTechnicalData(ComponentMaterial? Material, string? DeclaredMaterial, string? Reference,
    string? Description, string? OriginalDimensions, decimal? Measure, decimal? CutDimension,
    decimal? Width, decimal? Height, string? Unit, decimal? LeftAngle, decimal? RightAngle, int? Quantity,
    string? OriginalPieceId, string SpecificationsJson, string DiagnosticsJson);
public sealed record CandidateOrigin(int? Page, string Row, string? Sheet, string CoordinatesJson, string TemplateVersion, string ExtractorVersion);
public sealed class ProductionCandidateRow
{
    private ProductionCandidateRow() { }
    public ProductionCandidateRow(ProductionComponentRevision revision, PreparationSourceMember member, PreparationSourceDocument document,
        CandidateTechnicalData data, CandidateOrigin source)
    {
        if (revision.PreparationId != member.PreparationId || revision.PreparationId != document.PreparationId) throw new ArgumentException("Foreign candidate scope.");
        if (data.Quantity is < 1 || source.Page is < 1 || string.IsNullOrWhiteSpace(source.Row) || string.IsNullOrWhiteSpace(source.TemplateVersion) || string.IsNullOrWhiteSpace(source.ExtractorVersion)) throw new ArgumentException("Invalid candidate.");
        IncrementalEvidence.Json(data.SpecificationsJson); IncrementalEvidence.Json(data.DiagnosticsJson); IncrementalEvidence.Json(source.CoordinatesJson);
        Id = Guid.NewGuid(); PreparationId = revision.PreparationId; ComponentRevisionId = revision.Id;
        StructureId = member.StructureId; Op = member.Op; DocumentId = document.DocumentId; DocumentSha256 = document.Sha256;
        Material = data.Material; DeclaredMaterial = data.DeclaredMaterial;
        ProposedRoute = data.Material switch { ComponentMaterial.Glass => FabricationRoute.Glass, ComponentMaterial.AluminumProfile or ComponentMaterial.MicroperforatedSheet => FabricationRoute.Aluminum, null => null, _ => throw new ArgumentException("Unknown material.") };
        Reference = data.Reference; Description = data.Description; OriginalDimensions = data.OriginalDimensions;
        Measure = data.Measure; CutDimension = data.CutDimension; Width = data.Width; Height = data.Height; Unit = data.Unit;
        LeftAngle = data.LeftAngle; RightAngle = data.RightAngle; Quantity = data.Quantity; OriginalPieceId = data.OriginalPieceId;
        SpecificationsJson = data.SpecificationsJson; DiagnosticsJson = data.DiagnosticsJson;
        Page = source.Page; SourceRow = source.Row; Sheet = source.Sheet; CoordinatesJson = source.CoordinatesJson;
        TemplateVersion = source.TemplateVersion; ExtractorVersion = source.ExtractorVersion;
    }
    public Guid Id { get; private set; }
    public Guid PreparationId { get; private set; }
    public Guid ComponentRevisionId { get; private set; }
    public Guid StructureId { get; private set; }
    public string Op { get; private set; } = "";
    public Guid DocumentId { get; private set; }
    public string DocumentSha256 { get; private set; } = "";
    public ComponentMaterial? Material { get; private set; }
    public string? DeclaredMaterial { get; private set; }
    public FabricationRoute? ProposedRoute { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public string? OriginalDimensions { get; private set; }
    public decimal? Measure { get; private set; }
    public decimal? CutDimension { get; private set; }
    public decimal? Width { get; private set; }
    public decimal? Height { get; private set; }
    public string? Unit { get; private set; }
    public decimal? LeftAngle { get; private set; }
    public decimal? RightAngle { get; private set; }
    public int? Quantity { get; private set; }
    public string? OriginalPieceId { get; private set; }
    public string SpecificationsJson { get; private set; } = "{}";
    public string DiagnosticsJson { get; private set; } = "{}";
    public int? Page { get; private set; }
    public string SourceRow { get; private set; } = "";
    public string? Sheet { get; private set; }
    public string CoordinatesJson { get; private set; } = "{}";
    public string TemplateVersion { get; private set; } = "";
    public string ExtractorVersion { get; private set; } = "";
}
