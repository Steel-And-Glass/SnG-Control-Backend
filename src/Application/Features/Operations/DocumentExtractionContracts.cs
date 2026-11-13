using System.Text.Json;
namespace SnG.Control.Application.Features.Operations;
public sealed class DocumentExtractionOptions
{
    public bool Enabled { get; set; }
    public string PythonExecutable { get; set; }="";
    public int MaxDocumentBytes { get; set; }=26214400;
    public int MaxPages { get; set; }=100;
    public int MemoryMb { get; set; }=512;
    public int TimeoutSeconds { get; set; }=90;
}
public static class DocumentExtractorVersion { public const string Current="p2.2.1"; }
public sealed record ExtractionInputDocument(Guid Id,string Kind,string Sha256,long Size,byte[] Bytes);
public sealed record ExtractionInput(string ManifestHash,string[] Ops,int MaxPages,ExtractionInputDocument[] Documents);
public interface IDocumentComponentProcessor { Task<JsonElement> ProcessAsync(ExtractionInput input,CancellationToken token); }
public sealed record ExtractionJobView(Guid Id,Guid PreparationId,string ExtractorVersion,string State,int Attempts,
    long Revision,DateTime RequestedAt,DateTime? CompletedAt,string? ErrorCode,JsonElement Report,JsonElement Events,int Generation,Guid? ComponentRevisionId);
public sealed record CandidatePage(int Skip,IReadOnlyList<JsonElement> Rows);
public interface IDocumentExtractionStore
{
    Task<ExtractionJobView> QueueAsync(Guid preparation,string actor,CancellationToken token);
    Task<ExtractionJobView?> ReadAsync(Guid preparation,CancellationToken token);
    Task<CandidatePage> CandidatesAsync(Guid preparation,int skip,CancellationToken token);
}
public sealed class ExtractionProcessingException(string code) : Exception(code) { public string Code { get; }=code; }
