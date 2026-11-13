namespace SnG.Control.Domain.Entities;

public sealed class MeasurementSketchVersion
{
    private MeasurementSketchVersion() { }
    public MeasurementSketchVersion(MeasurementStructure structure, long revision, string drawingJson, string webpSha256, DateTime at, string actor)
    {
        MeasurementChecklist.ValidateActor(at, actor);
        Id = Guid.NewGuid(); StructureId = structure.Id; ChecklistId = structure.ChecklistId; ProjectId = structure.ProjectId;
        Revision = revision; DrawingJson = drawingJson; WebpSha256 = webpSha256; CreatedAt = at; CreatedBySubject = actor;
    }
    public Guid Id { get; private set; }
    public Guid StructureId { get; private set; }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public long Revision { get; private set; }
    public string DrawingJson { get; private set; } = "";
    public string WebpSha256 { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public string StorageKey => $"projects/{ProjectId:D}/measurement-sketches/{StructureId:D}/{Id:D}.webp";
}
