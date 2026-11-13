namespace SnG.Control.Domain.Entities.Incremental;

public sealed class EngineeringPackage
{
    private EngineeringPackage() { }
    public EngineeringPackage(WorkPackageAssignment assignment, bool isIntake, string actor, Guid? taskId = null)
    {
        MeasurementChecklist.ValidateActor(assignment.AssignedAt, actor);
        if (assignment.Area != 5 || assignment.EndedAt != null) throw new ArgumentException("Active Engineering assignment required.");
        Id=assignment.PackageId; ProjectId=assignment.ProjectId; CurrentAssignmentId=assignment.Id; IsIntake=isIntake;
        CreatedAt=assignment.AssignedAt; CreatedBySubject=actor; TaskId=taskId;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CurrentAssignmentId { get; private set; }
    public Guid? TaskId { get; private set; }
    public bool IsIntake { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBySubject { get; private set; } = "";
    public long Revision { get; private set; }
    public string Status { get; private set; } = "Open";
    public DateTime? CompletedAt { get; private set; }
    public void Advance() { if(Status!="Open") throw new InvalidOperationException("Package closed."); Revision=checked(Revision+1); }
    public void Reassign(WorkPackageAssignment assignment, Guid taskId)
    { Advance(); if(assignment.ProjectId!=ProjectId || assignment.PackageId!=Id || assignment.EndedAt!=null) throw new ArgumentException("Invalid assignment."); CurrentAssignmentId=assignment.Id; TaskId=taskId; }
    public void Close(DateTime at, bool transferred=false)
    { Advance(); if(at.Kind!=DateTimeKind.Utc || at<CreatedAt) throw new ArgumentException("UTC required."); Status=transferred?"Transferred":"Completed"; CompletedAt=at; }
}
public sealed class EngineeringAssignedStructure
{
    private EngineeringAssignedStructure() { }
    public EngineeringAssignedStructure(WorkPackageMember member)
    { ProjectId=member.ProjectId; StructureId=member.StructureId; SubmissionId=member.SubmissionId; EvidenceVersion=member.EvidenceVersion; AssignmentId=member.AssignmentId; }
    public Guid ProjectId { get; private set; }
    public Guid StructureId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int EvidenceVersion { get; private set; }
    public Guid AssignmentId { get; private set; }
    public void Assign(WorkPackageMember member)
    { if(member.ProjectId!=ProjectId || member.StructureId!=StructureId || member.SubmissionId!=SubmissionId || member.EvidenceVersion!=EvidenceVersion) throw new ArgumentException("Cannot replace evidence."); AssignmentId=member.AssignmentId; }
}
public sealed class EngineeringOpeningState
{
    private EngineeringOpeningState() { }
    public EngineeringOpeningState(OpeningRecord acta, string subject, string? createdBy = null)
    { MeasurementChecklist.ValidateActor(acta.CreatedAt,subject); MeasurementChecklist.ValidateActor(acta.CreatedAt,createdBy ?? subject); OpeningRecordId=acta.Id; ProjectId=acta.ProjectId; ResponsibleSubject=subject; CreatedBySubject=createdBy ?? subject; }
    public Guid OpeningRecordId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ResponsibleSubject { get; private set; } = "";
    public string CreatedBySubject { get; private set; } = "";
    public long Revision { get; private set; }
    public Guid? CurrentDocumentId { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public string? ConfirmedBySubject { get; private set; }
    public string? ConfirmationCanonical { get; private set; }
    public string? ConfirmationSha256 { get; private set; }
    public void Advance() { if(ConfirmedAt!=null) throw new InvalidOperationException("Acta confirmed."); Revision=checked(Revision+1); }
    public void Assign(string subject) { MeasurementChecklist.ValidateActor(DateTime.UnixEpoch,subject); Advance(); ResponsibleSubject=subject; }
    public void SetDocument(Guid documentId) { if(documentId==Guid.Empty) throw new ArgumentException("Document required."); Advance(); CurrentDocumentId=documentId; }
    public void Confirm(DateTime at,string subject,string canonical)
    { MeasurementChecklist.ValidateActor(at,subject); if(subject!=ResponsibleSubject || CurrentDocumentId==null) throw new InvalidOperationException("Invalid confirmation."); IncrementalEvidence.Json(canonical); Advance(); ConfirmedAt=at; ConfirmedBySubject=subject; ConfirmationCanonical=canonical; ConfirmationSha256=IncrementalEvidence.Hash(canonical); }
}
public sealed class EngineeringWorkspaceEvent
{
    private EngineeringWorkspaceEvent() { }
    public EngineeringWorkspaceEvent(Guid projectId,Guid key,string fingerprint,string action,Guid targetId,long revision,DateTime at,string subject,string canonical)
    { MeasurementChecklist.ValidateActor(at,subject); if(projectId==Guid.Empty || key==Guid.Empty || targetId==Guid.Empty || revision<0) throw new ArgumentException("Invalid event."); IncrementalEvidence.HashValue(fingerprint); IncrementalEvidence.Json(canonical);
      ProjectId=projectId;RequestKey=key;Fingerprint=fingerprint;Action=action;TargetId=targetId;Revision=revision;At=at;ActorSubject=subject;Canonical=canonical; }
    public Guid ProjectId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Action { get; private set; } = "";
    public Guid TargetId { get; private set; }
    public long Revision { get; private set; }
    public DateTime At { get; private set; }
    public string ActorSubject { get; private set; } = "";
    public string Canonical { get; private set; } = "";
}
