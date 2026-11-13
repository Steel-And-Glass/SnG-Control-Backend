namespace SnG.Control.Domain.Entities;
public sealed class MeasurementReport
{
    private MeasurementReport() { }
    public MeasurementReport(MeasurementChecklist checklist, byte[] pdf, string pdfSha256, string snapshotSha256)
    {
        if (checklist.SubmissionId == null || pdf.Length == 0) throw new ArgumentException("Submitted report required.");
        ChecklistId = checklist.Id; ProjectId = checklist.ProjectId; SubmissionId = checklist.SubmissionId.Value;
        SnapshotCanonical = checklist.SnapshotJson!; Version = 1; Pdf = pdf; PdfSha256 = pdfSha256; SnapshotSha256 = snapshotSha256;
    }
    public Guid ChecklistId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int Version { get; private set; }
    public byte[] Pdf { get; private set; } = [];
    public string SnapshotCanonical { get; private set; } = "";
    public string PdfSha256 { get; private set; } = "";
    public string SnapshotSha256 { get; private set; } = "";
}
