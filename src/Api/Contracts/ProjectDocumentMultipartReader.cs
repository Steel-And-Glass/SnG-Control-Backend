using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Api.Contracts;

public static class ProjectDocumentMultipartReader
{
    public const int ManifestMaxBytes = 16_384;
    public const long MaxRequestBytes = ProjectDocument.MaxBatchBytes + 1_000_000;
    private sealed record Manifest(IReadOnlyList<ManifestFile>? Files);
    private sealed record ManifestFile(string? PartName, Guid? ReplacesDocumentId);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private static DocumentException Invalid() => new(400, "InvalidDocumentBatch");
    public static async IAsyncEnumerable<DocumentUpload> ReadAsync(HttpRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) throw Invalid();
        var reader = new MultipartReader(boundary, request.Body) { HeadersCountLimit = 16, HeadersLengthLimit = 8192 };
        var manifestPart = await NextAsync(reader, token) ?? throw Invalid();
        var disposition = Disposition(manifestPart);
        if (HeaderUtilities.RemoveQuotes(disposition.Name).Value != "manifest"
            || !MediaTypeHeaderValue.TryParse(manifestPart.ContentType, out var jsonType)
            || !jsonType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || (jsonType.Charset.HasValue && !HeaderUtilities.RemoveQuotes(jsonType.Charset).Equals("utf-8", StringComparison.OrdinalIgnoreCase))) throw Invalid();
        var buffer = new byte[ManifestMaxBytes + 1]; var length = 0;
        try
        {
            int read;
            while (length < buffer.Length && (read = await manifestPart.Body.ReadAsync(buffer.AsMemory(length), token)) > 0) length += read;
        }
        catch (InvalidDataException) { throw Invalid(); }
        catch (IOException) { throw Invalid(); }
        if (length == 0 || length > ManifestMaxBytes) throw Invalid();
        Manifest? manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(buffer.AsSpan(0, length), Json); }
        catch (JsonException) { throw Invalid(); }
        if (manifest?.Files is not { Count: > 0 and <= ProjectDocument.MaxCurrentDocuments } files
            || files.Any(f => f is null || string.IsNullOrEmpty(f.PartName) || f.PartName.Length > 64 || f.PartName == "manifest"
                || f.PartName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || f.ReplacesDocumentId == Guid.Empty)
            || files.Select(f => f.PartName).Distinct(StringComparer.Ordinal).Count() != files.Count
            || files.Where(f => f.ReplacesDocumentId.HasValue).Select(f => f.ReplacesDocumentId).Distinct().Count() != files.Count(f => f.ReplacesDocumentId.HasValue)) throw Invalid();
        var remaining = files.ToDictionary(f => f.PartName!, StringComparer.Ordinal);
        MultipartSection? part;
        while ((part = await NextAsync(reader, token)) is not null)
        {
            disposition = Disposition(part);
            var partName = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (partName is null || !remaining.Remove(partName, out var file)) throw Invalid();
            var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
            if (name is null) throw Invalid();
            yield return new(partName, name, part.ContentType, file.ReplacesDocumentId, part.Body);
        }
        if (remaining.Count != 0) throw Invalid();
    }
    private static ContentDispositionHeaderValue Disposition(MultipartSection part)
    {
        if (!ContentDispositionHeaderValue.TryParse(part.ContentDisposition, out var value)
            || !value.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        return value;
    }
    private static async Task<MultipartSection?> NextAsync(MultipartReader reader, CancellationToken token)
    {
        try { return await reader.ReadNextSectionAsync(token); }
        catch (InvalidDataException) { throw Invalid(); }
        catch (IOException) { throw Invalid(); }
    }
}
