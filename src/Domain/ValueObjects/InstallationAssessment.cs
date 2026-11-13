namespace SnG.Control.Domain.ValueObjects;

public sealed record InstallationComparison(string Resolution, string Reason, string? MeasurementType, decimal? Width, decimal? Height);
public sealed record InstallationModification(Guid VersionId, string Type, string? Reason, string? Description, Guid? SketchVersionId, string Evidence);
public sealed record InstallationAssessmentChange(InstallationComparison? ComparisonBefore, InstallationComparison? ComparisonAfter,
    InstallationModification? ModificationBefore, InstallationModification? ModificationAfter);
