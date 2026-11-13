namespace SnG.Control.Application.Files;

// Only the response name changes. Historical metadata and workbook bytes stay intact.
public static class QuotationDownloadName
{
    public static string Sanitize(string original)
    {
        var leaf = original.Replace('\\', '/').Split('/').Last();
        var safe = new string(leaf.Select(c => char.IsControl(c) || ("<>:|?*".Contains(c) || c == (char)34) ? '_' : c).ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safe)) throw new InvalidOperationException("Quotation filename unavailable.");
        var extension = Path.GetExtension(safe);
        var stem = safe[..^extension.Length];
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            safe = "_" + safe;
        if (safe.Length > 200) safe = safe[..(200 - extension.Length)] + extension;
        return safe;
    }
}
