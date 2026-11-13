using Microsoft.Extensions.Options;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Storage;

public class DocumentStreamingStorageTests
{
    [Fact]
    public async Task StreamSaveAndRead_PreserveBytes_AndNeverOverwrite()
    {
        using var directory = new TemporaryStorage();
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory.Root }));
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        using var input = new MemoryStream(bytes);
        await storage.SaveFromStreamAsync("documents/content", input, default);
        await using var read = await storage.OpenReadAsync("documents/content", default);
        Assert.IsType<FileStream>(read); Assert.Equal(bytes.Length, read.Length);
        using var output = new MemoryStream(); await read.CopyToAsync(output); Assert.Equal(bytes, output.ToArray());
        input.Position = 0;
        await Assert.ThrowsAsync<IOException>(() => storage.SaveFromStreamAsync("documents/content", input, default));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.tmp", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("C:\\file")]
    [InlineData("a\\file")]
    [InlineData("a//file")]
    [InlineData("a:stream")]
    public async Task StreamingMethods_RejectUnsafeKeys(string key)
    {
        using var directory = new TemporaryStorage();
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory.Root }));
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<IOException>(() => storage.SaveFromStreamAsync(key, input, default));
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(key, default));
        Assert.Empty(Directory.GetFiles(directory.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancellationAndInputFailure_RemovePartialFiles()
    {
        using var directory = new TemporaryStorage();
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory.Root }));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var bytes = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveFromStreamAsync("nested/content", bytes, canceled.Token));
        using var broken = new BrokenStream();
        await Assert.ThrowsAsync<IOException>(() => storage.SaveFromStreamAsync("nested/content", broken, default));
        Assert.Empty(Directory.GetFiles(directory.Root, "*", SearchOption.AllDirectories));
    }
    private sealed class BrokenStream : MemoryStream
    {
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token) => throw new IOException("Synthetic read failure.");
    }
}
