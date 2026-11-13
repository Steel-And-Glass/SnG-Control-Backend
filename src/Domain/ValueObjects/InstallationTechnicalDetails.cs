namespace SnG.Control.Domain.ValueObjects;

// Left/Right means hinge side as seen from ViewSide. Changing ViewSide never flips it.
public sealed record InstallationTechnicalDetails(string? HandleStatus, decimal? HandleHeightMm,
    string? ViewSide, string? OpeningType, string? OpeningDirection, string? OpeningHand,
    bool? HasAlfajia, decimal? AlfajiaMeasurementMm)
{
    public void Validate(decimal? actualHeightMetres)
    {
        if (HandleStatus is not (null or "NotApplicable" or "Confirmed") || ViewSide is not (null or "Outside" or "Inside")
            || OpeningType is not (null or "NotApplicable" or "HingedDoor" or "HingedWindow" or "Other")
            || OpeningDirection is not (null or "Inward" or "Outward") || OpeningHand is not (null or "Left" or "Right")) Invalid();
        if (HandleStatus != "Confirmed" && HandleHeightMm != null) Invalid();
        if (HandleHeightMm is { } handle && (handle <= 0 || handle > 5000 || actualHeightMetres is null || handle / 1000m > actualHeightMetres)) Invalid();
        if (OpeningType is not ("HingedDoor" or "HingedWindow") && (OpeningDirection != null || OpeningHand != null)) Invalid();
        if (OpeningType != "HingedDoor" && OpeningHand != null) Invalid();
        if (HasAlfajia != true && AlfajiaMeasurementMm != null) Invalid();
        if (AlfajiaMeasurementMm is { } sill && (sill <= 0 || sill > 10000)) Invalid();
    }
    public bool IsComplete => HandleStatus is "NotApplicable" || HandleStatus == "Confirmed" && HandleHeightMm != null;
    public bool IsFullyComplete => IsComplete && ViewSide != null && OpeningType != null && HasAlfajia != null
        && (HasAlfajia != true || AlfajiaMeasurementMm != null)
        && (OpeningType is not ("HingedDoor" or "HingedWindow") || OpeningDirection != null)
        && (OpeningType != "HingedDoor" || OpeningHand != null);
    private static void Invalid() => throw new ArgumentException("Invalid installation technical details.");
}
