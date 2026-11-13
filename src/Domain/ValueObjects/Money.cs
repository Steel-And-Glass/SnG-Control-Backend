namespace SnG.Control.Domain.ValueObjects;

public static class Money
{
    public const string CurrencyCode = "COP";
    public const int Precision = 20;
    public const int Scale = 2;
    public const decimal Maximum = 999999999999999999.99m;

    public static decimal Normalize(decimal value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var normalized = decimal.Round(value, Scale, MidpointRounding.AwayFromZero);
        if (normalized > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
        return normalized;
    }
}
