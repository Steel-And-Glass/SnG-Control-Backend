using SnG.Control.Domain.Entities;

namespace SnG.Control.Application.Features.ProjectDocuments;

public sealed class DocumentBatchSize
{
    public long BytesRead { get; private set; }
    public void Add(int count)
    {
        BytesRead += count;
        if (BytesRead > ProjectDocument.MaxBatchBytes) throw new DocumentException(413, "BatchTooLarge");
    }
}

// Does not own the multipart stream. Counts actual reads, never trusts Content-Length.
public sealed class DocumentSizeStream(Stream source, DocumentBatchSize batch) : Stream
{
    public long BytesRead { get; private set; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        int count;
        try { count = await source.ReadAsync(buffer, token); }
        catch (InvalidDataException) { throw new DocumentException(400, "InvalidDocumentBatch"); }
        catch (IOException) { throw new DocumentException(400, "InvalidDocumentBatch"); }
        BytesRead += count;
        if (BytesRead > ProjectDocument.MaxFileBytes) throw new DocumentException(413, "FileTooLarge");
        batch.Add(count);
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
