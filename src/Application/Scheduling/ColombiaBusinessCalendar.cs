namespace SnG.Control.Application.Scheduling;

public sealed class ColombiaBusinessCalendar
{
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public bool IsBusinessDay(DateOnly date) => date.DayOfWeek != DayOfWeek.Sunday && !Holidays(date.Year).Contains(date);

    public DateOnly MoveBusinessDays(DateOnly date, int days)
    {
        var direction = Math.Sign(days);
        for (var remaining = Math.Abs(days); remaining > 0;)
        {
            date = date.AddDays(direction);
            if (IsBusinessDay(date)) remaining--;
        }
        return date;
    }

    public DateOnly LocalDate(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC instant is required.", nameof(utc));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    }

    public DateTime AtLocalTime(DateOnly date, int hour) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), Zone);

    public IReadOnlySet<DateOnly> Holidays(int year)
    {
        var result = new HashSet<DateOnly>();
        foreach (var (month, day) in new[] { (1, 1), (5, 1), (7, 20), (8, 7), (12, 8), (12, 25) }) result.Add(new(year, month, day));
        foreach (var (month, day) in new[] { (1, 6), (3, 19), (6, 29), (8, 15), (10, 12), (11, 1), (11, 11) }) result.Add(NextMonday(new(year, month, day)));
        // Ley 2578/2026, article 6: national holiday, observed under Ley 51/1983.
        if (year >= 2026) result.Add(NextMonday(new(year, 7, 9)));
        var easter = EasterSunday(year);
        result.Add(easter.AddDays(-3)); result.Add(easter.AddDays(-2));
        foreach (var offset in new[] { 39, 60, 68 }) result.Add(NextMonday(easter.AddDays(offset)));
        return result;
    }

    private static DateOnly NextMonday(DateOnly date) => date.AddDays((8 - (int)date.DayOfWeek) % 7);

    // Meeus/Jones/Butcher Gregorian computus; no yearly tables or network calls.
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19; var b = year / 100; var c = year % 100;
        var d = b / 4; var e = b % 4; var f = (b + 8) / 25; var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30; var i = c / 4; var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7; var m = (a + 11 * h + 22 * l) / 451;
        var value = h + l - 7 * m + 114;
        return new(year, value / 31, value % 31 + 1);
    }
}
