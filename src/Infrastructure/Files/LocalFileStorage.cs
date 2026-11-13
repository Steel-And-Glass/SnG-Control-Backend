using Microsoft.Extensions.Options;
using SnG.Control.Application.Files;

namespace SnG.Control.Infrastructure.Files;

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IStreamingFileStorage
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.RootPath));
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Storage file links are not supported.");
        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task SaveAsync(string storageKey, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        WriteAsync(storageKey, (output, token) => output.WriteAsync(bytes, token).AsTask(), cancellationToken);

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Storage file links are not supported.");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public Task SaveFromStreamAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
        WriteAsync(storageKey, (output, token) => content.CopyToAsync(output, 81920, token), cancellationToken);

    private async Task WriteAsync(string storageKey, Func<FileStream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        var destination = Resolve(storageKey);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        RejectLinks(directory);
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await write(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        RejectLinks(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Storage file links are not supported.");
        File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey) || storageKey.Contains('\\') || storageKey.Contains(':')
            || storageKey.Split('/').Any(segment => segment is "" or "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new IOException("Storage key must be a safe relative key.");
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison))
            throw new IOException("Storage key escapes its root.");
        RejectLinks(Path.GetDirectoryName(path)!);
        return path;
    }

    private void RejectLinks(string directory)
    {
        // The storage root must be service-owned; reject existing junctions/symlinks within it.
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Storage directory links are not supported.");
            if (string.Equals(current.FullName, root, PathComparison)) break;
        }
    }
}
