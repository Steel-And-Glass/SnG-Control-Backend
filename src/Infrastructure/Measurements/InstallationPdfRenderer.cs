using System.Globalization;
using SkiaSharp;
using SnG.Control.Application.Features.Measurements;
namespace SnG.Control.Infrastructure.Measurements;

public sealed record InstallationReportRenderData(MeasurementSnapshot Snapshot,
    IReadOnlyDictionary<Guid, byte[]> Sketches, IReadOnlyDictionary<Guid, byte[]> QuotationImages);

// All assets are embedded from validated historical bytes. No database or external URL access.
public sealed class InstallationPdfRenderer
{
    private const float PageWidth = 1190.55f, PageHeight = 841.89f, Margin = 42, Bottom = 777;
    private const float LeftWidth = 365, CenterWidth = 365, LineHeight = 10;
    public const int MaxImageBytes = 16 * 1024 * 1024;
    private const long MaxImagePixels = 24_000_000;

    public byte[] Render(MeasurementSnapshot snapshot, IReadOnlyDictionary<Guid, byte[]> sketches, CancellationToken token) =>
        Render(new(snapshot, sketches, new Dictionary<Guid, byte[]>()), token);

    public byte[] Render(InstallationReportRenderData data, CancellationToken token)
    {
        var snapshot = data.Snapshot;
        var header = snapshot.Report ?? throw new ArgumentException("Report header required.");
        using var fontStream = typeof(InstallationPdfRenderer).Assembly.GetManifestResourceStream("SnG.Control.Infrastructure.Measurements.Fonts.NotoSans-Regular.ttf") ?? throw new InvalidOperationException("Report font missing.");
        using var face = SKTypeface.FromStream(fontStream);
        using var font = new SKFont(face, 8.5f);
        using var labelFont = new SKFont(face, 7.5f) { Embolden = true };
        using var heading = new SKFont(face, 9) { Embolden = true };
        using var small = new SKFont(face, 6.5f);
        using var title = new SKFont(face, 18) { Embolden = true };
        using var ink = new SKPaint { Color = SKColor.Parse("#172D3F"), IsAntialias = true };
        using var muted = new SKPaint { Color = SKColor.Parse("#597087"), IsAntialias = true };
        using var green = new SKPaint { Color = SKColor.Parse("#247751"), IsAntialias = true };
        using var orange = new SKPaint { Color = SKColor.Parse("#AF5A0A"), IsAntialias = true };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var background = new SKPaint { Color = SKColor.Parse("#F6F9FB"), IsAntialias = true };
        using var border = new SKPaint { Color = SKColor.Parse("#CADBE7"), Style = SKPaintStyle.Stroke, StrokeWidth = .7f, IsAntialias = true };
        using var logoStream = typeof(InstallationPdfRenderer).Assembly.GetManifestResourceStream("SnG.Control.Infrastructure.Measurements.SteelAndGlassLogo.png") ?? throw new InvalidOperationException("Report logo missing.");
        using var logoData = SKData.Create(logoStream);
        using var logo = SKImage.FromEncodedData(logoData) ?? throw new InvalidOperationException("Report logo invalid.");
        var rightX = Margin + LeftWidth + CenterWidth;
        var rightWidth = PageWidth - Margin - rightX;
        const float labelWidth = 116;
        var projectLines = Wrap(header.ProjectName, heading, 230).ToArray();
        var projectHeight = 30 + projectLines.Length * LineHeight;
        var metadata = new[] {
            $"Cliente: {header.ClientName} | Responsable: {header.ResponsibleName} | Radicación: {header.SubmittedAt:yyyy-MM-dd HH:mm} UTC",
            $"Cotizadas: {snapshot.Summary.Initial} | Vigentes: {snapshot.Summary.Active} | Agregadas: {snapshot.Summary.Added} | Retiradas: {snapshot.Summary.Retired} | Reintegradas: {header.Reinstated} | Longitudes en mm"
        }.SelectMany(v => Wrap(v, font, PageWidth - 2 * Margin)).ToArray();
        var metadataTop = Math.Max(88, 32 + projectHeight + 10);
        var barTop = metadataTop + metadata.Length * LineHeight + 8;
        var top = barTop + 42;
        const float gap = 10;
        var rowHeight = (Bottom - top - 2 * gap) / 3;
        var capacity = (int)((rowHeight - 45) / LineHeight);
        if (capacity < 7) throw new InvalidOperationException("Report header exceeds available space.");
        var pages = new List<List<Row>>();
        var current = new List<Row>();
        foreach (var structure in snapshot.Structures)
        {
            token.ThrowIfCancellationRequested();
            var lines = TechnicalFields(structure).SelectMany(field =>
                Wrap(field.Wide ? "Observaciones: " + field.Value : field.Value, font, rightWidth - 30 - (field.Wide ? 0 : labelWidth))
                    .Select((value, i) => new Field(i == 0 ? field.Label : "", value, field.Wide))).ToArray();
            var identity = (structure.ItemId == null ? "AGREGADA EN OBRA" : "ÍTEM " + Text(structure.ItemNumber)) +
                $"  ·  UNIDAD {structure.UnitOrdinal}/{structure.UnitCount}";
            var identityLines = Wrap(identity, heading, LeftWidth - 22).ToArray();
            var versions = new[] { structure.SketchVersionId, structure.Modification?.SketchVersionId }.OfType<Guid>().Distinct().ToArray();
            var offset = 0; var part = 0;
            do
            {
                if (part > 0 && current.Count > 0) { pages.Add(current); current = []; }
                var bodyCapacity = part == 0 ? capacity : (int)((Bottom - top - 45) / LineHeight);
                current.Add(new(structure, lines.Skip(offset).Take(bodyCapacity).ToArray(), identityLines, part,
                    versions.Length == 0 ? null : versions[Math.Min(part, versions.Length - 1)]));
                offset += bodyCapacity;
                part++;
                if (current.Count == 3 || part > 1 || offset < lines.Length || part < versions.Length) { pages.Add(current); current = []; }
            } while (offset < lines.Length || part < versions.Length);
        }
        if (current.Count > 0) pages.Add(current);
        if (pages.Count == 0) pages.Add([]);
        using var output = new MemoryStream();
        // A zero-initialized metadata struct selects JPEG quality 0. Start from Skia's
        // defaults (lossless encoding) so historical PNG/WEBP pixels and alpha survive embedding.
        var pdfMetadata = SKDocumentPdfMetadata.Default;
        pdfMetadata.Title = "Reporte de Toma de Medidas - Steel & Glass";
        pdfMetadata.Author = "Steel & Glass";
        pdfMetadata.Creation = header.SubmittedAt;
        using var document = SKDocument.CreatePdf(output, pdfMetadata);
        for (var page = 0; page < pages.Count; page++)
        {
            token.ThrowIfCancellationRequested();
            var canvas = document.BeginPage(PageWidth, PageHeight);
            canvas.DrawRect(0, 0, PageWidth, barTop - 5, background);
            canvas.DrawImage(logo, SKRect.Create(50, 43, 205, 205f * logo.Height / logo.Width));
            canvas.DrawText("REPORTE DE TOMA DE MEDIDAS", 290, 47, title, ink);
            canvas.DrawText("Fichas por estructura · distribución de 3 ítems por página", 290, 70, font, muted);
            var projectBox = SKRect.Create(PageWidth - Margin - 252, 32, 252, projectHeight);
            canvas.DrawRoundRect(projectBox, 7, 7, white); canvas.DrawRoundRect(projectBox, 7, 7, border);
            var y = 48f;
            foreach (var line in projectLines) { canvas.DrawText(line, projectBox.Left + 12, y, heading, ink); y += LineHeight; }
            canvas.DrawText($"{Text(header.SgCode)} · Radicado · v{header.Version}", projectBox.Left + 12, y + 6, font, muted);
            y = metadataTop;
            foreach (var line in metadata) { canvas.DrawText(line, Margin, y, font, muted); y += LineHeight; }
            canvas.DrawRoundRect(SKRect.Create(Margin, barTop, PageWidth - 2 * Margin, 26), 5, 5, ink);
            canvas.DrawText("CROQUIS DE INSTALACIÓN", Margin + 15, barTop + 17, heading, white);
            canvas.DrawText("IMAGEN ORIGINAL DE COTIZACIÓN", Margin + LeftWidth + 15, barTop + 17, heading, white);
            canvas.DrawText("FICHA TÉCNICA DE LA ESTRUCTURA", rightX + 15, barTop + 17, heading, white);
            for (var rowIndex = 0; rowIndex < pages[page].Count; rowIndex++)
            {
                var row = pages[page][rowIndex]; var st = row.Structure;
                var rowTop = top + rowIndex * (rowHeight + gap);
                var height = row.Part > 0 ? Bottom - top : rowHeight;
                canvas.DrawRoundRect(SKRect.Create(Margin, rowTop, PageWidth - 2 * Margin, height), 7, 7, border);
                canvas.DrawLine(Margin + LeftWidth, rowTop, Margin + LeftWidth, rowTop + height, border);
                canvas.DrawLine(rightX, rowTop, rightX, rowTop + height, border);
                y = rowTop + 20;
                foreach (var line in row.Identity) { canvas.DrawText(line, Margin + 11, y, heading, ink); y += LineHeight; }
                canvas.DrawText("EXTRAÍDA DEL EXCEL", Margin + LeftWidth + 10, rowTop + 20, labelFont, muted);
                canvas.DrawText(row.Part > 0 ? "CONTINUACIÓN DE FICHA" : "DATOS REGISTRADOS EN OBRA", rightX + 14, rowTop + 20, labelFont, ink);
                var status = st.RetiredAt != null ? "RETIRADA" : st.Modification != null ? "MODIFICACIÓN" :
                    st.ComparisonStatus == "NoDimensionalChange" ? "SIN MOD. DIMENSIONAL" : "REVISIÓN PENDIENTE";
                var statusPaint = st.Modification != null || st.ComparisonStatus != "NoDimensionalChange" ? orange : green;
                canvas.DrawText(status, PageWidth - Margin - 13 - labelFont.MeasureText(status), rowTop + 20, labelFont, statusPaint);
                y = rowTop + 37;
                foreach (var line in row.Lines)
                {
                    if (line.Wide)
                    {
                        if (line.Label.Length > 0) canvas.DrawLine(rightX + 14, y - 8, PageWidth - Margin - 14, y - 8, border);
                        canvas.DrawText(line.Value, rightX + 14, y, font, muted);
                    }
                    else
                    {
                        canvas.DrawText(line.Label, rightX + 14, y, labelFont, muted);
                        canvas.DrawText(line.Value, rightX + 14 + labelWidth, y, font, line.Label is "MODIFICACIÓN" or "MOTIVO" ? orange : ink);
                    }
                    y += LineHeight;
                }
                canvas.DrawText("Estructura " + st.Id, rightX + 14, rowTop + height - 8, small, muted);
                var imageBox = new SKRect(Margin + 11, rowTop + 30 + (row.Identity.Length - 1) * LineHeight, Margin + LeftWidth - 11, rowTop + height - 18);
                if (imageBox.Height < 25) throw new InvalidOperationException("Structure identity exceeds available space.");
                canvas.DrawRoundRect(imageBox, 5, 5, background); canvas.DrawRoundRect(imageBox, 5, 5, border);
                var contentBox = imageBox; contentBox.Inflate(-8, -5);
                if (row.SketchVersion is { } version)
                {
                    if (!data.Sketches.TryGetValue(version, out var bytes)) throw new InvalidOperationException("Snapshot sketch missing.");
                    DrawImage(canvas, bytes, contentBox, true);
                    canvas.DrawText("Croquis " + version, Margin + 11, rowTop + height - 7, small, muted);
                }
                else DrawMessage(canvas, "Sin croquis de obra", contentBox, font, muted);
                imageBox.Offset(LeftWidth, 0); contentBox.Offset(LeftWidth, 0);
                canvas.DrawRoundRect(imageBox, 5, 5, background); canvas.DrawRoundRect(imageBox, 5, 5, border);
                if (st.ItemId is { } itemId && data.QuotationImages.TryGetValue(itemId, out var commercial)) DrawImage(canvas, commercial, contentBox, false);
                else DrawMessage(canvas, st.ItemId == null ? "Estructura agregada en obra - sin imagen de cotización" : "Imagen histórica de cotización no disponible", contentBox, font, muted);
            }
            canvas.DrawLine(Margin, 802, PageWidth - Margin, 802, border);
            canvas.DrawText($"{Text(header.SgCode)} · Radicación {header.SubmissionId} · Versión {header.Version}{(header.TrancheNumber is {} number ? $" / Tramo {number:00}" : "")}", Margin, 818, small, muted);
            var footer = $"EXPEDIENTE RADICADO · {page + 1} / {pages.Count}";
            canvas.DrawText(footer, PageWidth - Margin - labelFont.MeasureText(footer), 818, labelFont, muted);
            document.EndPage();
        }
        document.Close(); return output.ToArray();
    }

    private sealed record Field(string Label, string Value, bool Wide = false);
    private sealed record Row(MeasurementStructureView Structure, Field[] Lines, string[] Identity, int Part, Guid? SketchVersion);

    private static IEnumerable<Field> TechnicalFields(MeasurementStructureView s)
    {
        if (!s.TechnicalComplete) yield return new("ESTADO", "Ficha histórica incompleta");
        if (s.Location == s.ActualLocation) yield return new("UBICACIÓN", Text(s.Location));
        else { yield return new("UBICACIÓN COT.", Text(s.Location)); yield return new("UBICACIÓN OBRA", Text(s.ActualLocation)); }
        if (s.Description == s.ActualSystemText) yield return new("SISTEMA", Text(s.Description));
        else { yield return new("SISTEMA COT.", Text(s.Description)); yield return new("SISTEMA OBRA", Text(s.ActualSystemText)); }
        yield return new("MEDIDA", Label(s.MeasurementType));
        yield return new("COTIZADA", $"{Mm(s.Width)} × {Mm(s.Height)} mm");
        yield return new("EN OBRA", $"{Mm(s.ActualWidth)} × {Mm(s.ActualHeight)} mm");
        var t = s.Technical;
        yield return new("MANIJA", t?.HandleStatus == "Confirmed" ? $"{N(t.HandleHeightMm)} mm · Confirmada" : Label(t?.HandleStatus));
        yield return new("VISTA", Label(t?.ViewSide));
        yield return new("APERTURA", Label(t?.OpeningType));
        if (t?.OpeningDirection != null || t?.OpeningHand != null) yield return new("DIR. / BISAGRA", $"{Label(t?.OpeningDirection)} / {Label(t?.OpeningHand)}");
        yield return new("ALFAJÍA", t?.HasAlfajia is true ? N(t.AlfajiaMeasurementMm) + " mm" : t?.HasAlfajia is false ? "No lleva" : "Sin registrar");
        yield return new("COMPARACIÓN", s.ComparisonStatus switch {
            "OverThreshold" => "Diferencia superior a 100 mm",
            "NoDimensionalChange" => "Sin cambio dimensional identificado",
            _ => "Pendiente; no implica ausencia de modificación"
        });
        if (s.WidthDifferenceMm != null) yield return new("DIF. ABSOLUTAS", $"{N(s.WidthDifferenceMm)} / {N(s.HeightDifferenceMm)} mm");
        if (s.Comparison != null) yield return new("REVISIÓN", $"{Label(s.Comparison.Resolution)} · {s.Comparison.Reason}");
        if (s.Modification is { } m)
        {
            yield return new("MODIFICACIÓN", $"{Label(m.Type)} · {m.VersionId}");
            yield return new("MOTIVO", Text(m.Reason));
            yield return new("DESCRIPCIÓN", Text(m.Description));
            yield return new("CROQUIS MOD.", m.SketchVersionId?.ToString() ?? "Sin registrar");
        }
        if (!string.IsNullOrWhiteSpace(s.InstallationObservations)) yield return new("OBSERVACIONES", s.InstallationObservations, true);
    }

    private static void DrawImage(SKCanvas canvas, byte[] bytes, SKRect box, bool sketch)
    {
        if (bytes.Length > MaxImageBytes) throw new InvalidOperationException("Report image exceeds byte limit.");
        using var encoded = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidOperationException("Report image cannot be decoded.");
        var size = codec.Info;
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > MaxImagePixels ||
            sketch && (size.Width != 1200 || size.Height != 900)) throw new InvalidOperationException("Invalid report image dimensions.");
        using var image = SKImage.FromEncodedData(encoded) ?? throw new InvalidOperationException("Report image cannot be decoded.");
        var scale = Math.Min(box.Width / size.Width, box.Height / size.Height);
        var width = size.Width * scale; var height = size.Height * scale;
        canvas.DrawImage(image, SKRect.Create(box.MidX - width / 2, box.MidY - height / 2, width, height));
    }
    private static void DrawMessage(SKCanvas canvas, string text, SKRect box, SKFont font, SKPaint paint)
    {
        var lines = Wrap(text, font, box.Width).ToArray(); var y = box.MidY - lines.Length * LineHeight / 2;
        foreach (var line in lines) { canvas.DrawText(line, box.Left, y, font, paint); y += LineHeight; }
    }
    private static IEnumerable<string> Wrap(string value, SKFont font, float width)
    {
        foreach (var paragraph in value.Replace("\r", "").Split('\n'))
        {
            var line = "";
            var elements = StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (font.MeasureText(line + element) > width && line.Length > 0)
                {
                    var split = line.LastIndexOf(' ');
                    yield return split > 0 ? line[..split] : line;
                    line = split > 0 ? line[(split + 1)..] : "";
                }
                line += element;
            }
            yield return line;
        }
    }
    private static string N(decimal? n) => n?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Sin registrar";
    private static string Mm(decimal? n) => N(n * 1000m);
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Sin registrar" : value;
    private static string Label(string? value) => value switch {
        "Agreed" => "Acordada", "Production" => "Producción", "Opening" => "Vano", "NotApplicable" => "No aplica", "Confirmed" => "Confirmada",
        "Outside" => "Por fuera", "Inside" => "Por dentro", "HingedDoor" => "Puerta batiente", "HingedWindow" => "Ventana batiente",
        "Other" => "Otro", "Inward" => "Hacia adentro", "Outward" => "Hacia afuera", "Left" => "Izquierda", "Right" => "Derecha",
        "Comparable" => "Comparable", "NotComparable" => "No comparable", "Dimensional" => "Dimensional", "System" => "Sistema",
        "Typology" => "Tipología", "Configuration" => "Configuración", "Structural" => "Estructural", _ => Text(value) };
}
