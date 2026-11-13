using System.Text.Json;
namespace SnG.Control.Domain.Entities.Production;
public enum ExtractionJobState { Queued=1, Running=2, Completed=3, Blocked=4, Failed=5 }
public sealed class DocumentExtractionJob
{
    private DocumentExtractionJob() { }
    public DocumentExtractionJob(Guid preparationId,string version,string actor,DateTime at,int generation=1,string? reason=null)
    { Generation=generation;ReextractReason=reason;Id=Guid.NewGuid();PreparationId=preparationId;ExtractorVersion=version;RequestedBySubject=actor;RequestedAt=at; }
    public int Generation {get;private set;}=1;
    public string? ReextractReason {get;private set;}
    public Guid? ComponentRevisionId {get;private set;}
    public void SetResultRevision(Guid id){if(State!=ExtractionJobState.Running)throw new InvalidOperationException();ComponentRevisionId=id;}
    public Guid Id { get; private set; }
    public Guid PreparationId { get; private set; }
    public string ExtractorVersion { get; private set; }="";
    public string RequestedBySubject { get; private set; }="";
    public DateTime RequestedAt { get; private set; }
    public ExtractionJobState State { get; private set; }=ExtractionJobState.Queued;
    public int Attempts { get; private set; }
    public long Revision { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeaseUntil { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string ReportJson { get; private set; }="{}";
    public string EventsJson { get; private set; }="[]";
    public bool Retry(string actor,DateTime at)
    {
        if(State!=ExtractionJobState.Failed || Attempts>=3)return false;
        State=ExtractionJobState.Queued;Revision++;Event("RetryRequested",actor,at);return true;
    }
    public Guid Claim(DateTime at,int seconds)
    {
        if(Attempts>=3 || !(State==ExtractionJobState.Queued || State==ExtractionJobState.Running && LeaseUntil<=at))throw new InvalidOperationException();
        State=ExtractionJobState.Running;Attempts++;Revision++;LeaseToken=Guid.NewGuid();LeaseUntil=at.AddSeconds(seconds);
        Event("Claimed",null,at);return LeaseToken.Value;
    }
    public void Finish(ExtractionJobState state,string report,string? error,DateTime at)
    {
        if(State!=ExtractionJobState.Running || state is not (ExtractionJobState.Completed or ExtractionJobState.Blocked or ExtractionJobState.Failed))throw new InvalidOperationException();
        State=state;ReportJson=report;ErrorCode=error;CompletedAt=at;LeaseUntil=null;LeaseToken=null;Revision++;Event(error??state.ToString(),null,at);
    }
    private void Event(string code,string? actor,DateTime at)
    {
        var events=JsonSerializer.Deserialize<List<JsonElement>>(EventsJson)!;
        events.Add(JsonSerializer.SerializeToElement(new {code,actor,at,attempt=Attempts}));EventsJson=JsonSerializer.Serialize(events);
    }
}
