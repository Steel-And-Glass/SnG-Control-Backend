using System.IO.Compression;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Files;
using SnG.Control.Domain.Entities;

namespace SnG.Control.Infrastructure.Files;

public sealed class ProjectDocumentArchive(IStreamingFileStorage storage) : IProjectDocumentArchive
{
    public async Task<Stream> CreateAsync(IReadOnlyList<ProjectDocument> documents, CancellationToken token)
    {
        // Bounded RAM. Private, random, delete-on-close scratch file; never a canonical copy.
        var output = new FileStream(Path.Combine(Path.GetTempPath(), "sng-documents-" + Guid.NewGuid().ToString("N") + ".zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var document in documents)
                {
                    token.ThrowIfCancellationRequested();
                    ProjectDocument.ValidateFileName(document.OriginalFileName);
                    var name = document.OriginalFileName; var number = 2;
                    while (!used.Add(name))
                    {
                        var extension = Path.GetExtension(document.OriginalFileName);
                        var stem = Path.GetFileNameWithoutExtension(document.OriginalFileName);
                        name = $"{stem} ({number++}){extension}";
                    }
                    await using var source = await storage.OpenReadAsync(document.StorageKey, token);
                    await using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    await source.CopyToAsync(entry, 81920, token);
                }
            }
            output.Position = 0;
            return output;
        }
        catch { await output.DisposeAsync(); throw; }
    }
}
