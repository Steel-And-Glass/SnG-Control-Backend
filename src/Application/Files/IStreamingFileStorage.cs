namespace SnG.Control.Application.Files;

// Streaming capability alongside the existing quotation byte-array contract.
public interface IStreamingFileStorage : IFileStorage
{
    Task SaveFromStreamAsync(string storageKey, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
}
