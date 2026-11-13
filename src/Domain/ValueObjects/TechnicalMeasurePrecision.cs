namespace SnG.Control.Domain.ValueObjects;

// Comparison/display precision only; never normalize persisted source measurements.
// Midpoints round away from zero. This is not a fabrication/installation tolerance.
public static class TechnicalMeasurePrecision
{
    public const int DimensionScale = 3;
    public const int SurfaceScale = 6;
    public static decimal Dimension(decimal value) => decimal.Round(value, DimensionScale, MidpointRounding.AwayFromZero);
    public static decimal Surface(decimal value) => decimal.Round(value, SurfaceScale, MidpointRounding.AwayFromZero);
}
