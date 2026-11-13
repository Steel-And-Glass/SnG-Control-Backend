using System.Buffers.Binary;
using System.Text.Json;
using SnG.Control.Application.Features.ProjectDocuments;

namespace SnG.Control.Application.Features.Measurements;

public sealed record SketchPoint(double X, double Y);
public sealed record SketchStroke(string Tool, string Color, double Width, SketchPoint[] Points);
public sealed record SketchAnnotation(Guid Id, string Content, double X, double Y, int Size, string Color, int Order);
public sealed record SketchDrawing(int Version, int Width, int Height, SketchStroke[] Strokes,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchAnnotation[]? Annotations = null);
public sealed record SketchUpload(SketchDrawing Drawing, byte[] Webp);
public sealed record SketchRead(Guid Id, Guid StructureId, long Revision, SketchDrawing Drawing, string WebpSha256);
public static class MeasurementSketch
{
    public const int MaxRequestBytes = 2_000_000;
    public static void Validate(SketchUpload upload)
    {
        var d = upload.Drawing; var bytes = upload.Webp;
        if (d is null || d.Version is not (1 or 2) || d.Width != 1200 || d.Height != 900 || d.Strokes is null || d.Strokes.Length > 200) Invalid();
        var count = 0;
        foreach (var stroke in d!.Strokes)
        {
            if (stroke is null || stroke.Tool is not ("Pen" or "Eraser") || stroke.Color is not ("#111111" or "#0066cc" or "#cc2222")
                || !double.IsFinite(stroke.Width) || stroke.Width < 1 || stroke.Width > 20 || stroke.Points is null || stroke.Points.Length is < 1 or > 2000) Invalid();
            count += stroke!.Points.Length;
            if (count > 20000 || stroke.Points.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1)) Invalid();
        }
        if (d.Version == 1 && d.Annotations != null || d.Version == 2 && d.Annotations == null) Invalid();
        var annotations = d.Annotations ?? [];
        if (annotations.Length > 50 || annotations.Any(a => a == null || a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Content) || a.Content.Length > 300 || a.Content.Any(char.IsControl) || !double.IsFinite(a.X) || !double.IsFinite(a.Y) || a.X < 0 || a.X > 1 || a.Y < 0 || a.Y > 1 || a.Size < 12 || a.Size > 72 || a.Color is not ("#111111" or "#0066cc" or "#cc2222") || a.Order < 0 || a.Order >= 50)
            || annotations.Select(a => a.Id).Distinct().Count() != annotations.Length || annotations.Select(a => a.Order).Distinct().Count() != annotations.Length) Invalid();
        if (JsonSerializer.SerializeToUtf8Bytes(d).Length > 1_000_000 || bytes is null || bytes.Length is < 30 or > 512_000) Invalid();
        // Canvas may include an ICC color profile: VP8X + ICCP + VP8.
        // Accept only that opaque, nonanimated variant or the historical simple VP8 container.
        // https://developers.google.com/speed/webp/docs/riff_container
        var b = bytes!.AsSpan();
        if (!b[..4].SequenceEqual("RIFF"u8) || !b.Slice(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(b[4..]) != b.Length - 8) Invalid();
        var offset = 12;
        if (b.Slice(offset, 4).SequenceEqual("VP8X"u8))
        {
            var extended = Chunk(b, ref offset, "VP8X"u8);
            if (extended.Length != 10 || extended[0] != 0x20 || extended[1] != 0 || extended[2] != 0 || extended[3] != 0
                || UInt24(extended[4..]) + 1 != d.Width || UInt24(extended[7..]) + 1 != d.Height) Invalid();
            var profile = Chunk(b, ref offset, "ICCP"u8);
            if (profile.Length < 128 || BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length
                || !profile.Slice(36, 4).SequenceEqual("acsp"u8)) Invalid();
        }
        var frame = Chunk(b, ref offset, "VP8 "u8);
        if (offset != b.Length || frame.Length < 10 || (frame[0] & 1) != 0
            || !frame.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a })
            || (BinaryPrimitives.ReadUInt16LittleEndian(frame[6..]) & 0x3fff) != d.Width
            || (BinaryPrimitives.ReadUInt16LittleEndian(frame[8..]) & 0x3fff) != d.Height) Invalid();
    }
    private static int UInt24(ReadOnlySpan<byte> value) => value[0] | value[1] << 8 | value[2] << 16;
    private static ReadOnlySpan<byte> Chunk(ReadOnlySpan<byte> bytes, ref int offset, ReadOnlySpan<byte> kind)
    {
        if (offset > bytes.Length - 8 || !bytes.Slice(offset, 4).SequenceEqual(kind)) Invalid();
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
        if ((long)length + (length % 2) > bytes.Length - offset - 8) Invalid();
        var payload = bytes.Slice(offset + 8, (int)length);
        offset += 8 + (int)length;
        if (length % 2 != 0 && bytes[offset++] != 0) Invalid();
        return payload;
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new DocumentException(400, "InvalidMeasurementSketch");
}
