using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;

namespace SnG.Control.Infrastructure.Persistence.Configurations;

internal static class IncrementalFlowConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        EngineeringOperationsConfiguration.Configure(model);
        CommercialOpeningConfiguration.Configure(model);
        // Composite keys only: legacy uniqueness and operational semantics are preserved.
        model.Entity<MeasurementStructure>().HasAlternateKey(x => new { x.Id, x.ProjectId });
        model.Entity<ProjectTask>().HasAlternateKey(x => new { x.Id, x.ProjectId });
        model.Entity<MeasurementReport>().HasAlternateKey(x => new { x.ChecklistId, x.ProjectId, x.SubmissionId });
        model.Entity<EngineeringStructure>().HasAlternateKey(x => new { x.ChecklistId, x.StructureId, x.ProjectId });
        model.Entity<MeasurementSubmission>(b =>
        {
            b.ToTable("MeasurementSubmissions", t => t.HasCheckConstraint("CK_MeasurementSubmission", "(\"Number\" > 0 AND \"EvidenceStatus\" IN ('COMPLETE','LEGACY_INCOMPLETE_EVIDENCE') AND \"ReportAvailability\" IN ('PRESENT','ABSENT') AND (\"IsLegacy\" OR (\"EvidenceStatus\"='COMPLETE' AND \"RequestKey\" IS NOT NULL AND \"Fingerprint\" IS NOT NULL))) AND (length(btrim(\"SubmittedBySubject\")) > 0) AND (length(btrim(\"EvidenceStatus\")) > 0) AND (length(btrim(\"ReportAvailability\")) > 0) AND (\"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.SubmittedBySubject).HasMaxLength(200);
            b.Property(x => x.EvidenceStatus).HasMaxLength(200);
            b.Property(x => x.ScopeIssue).HasMaxLength(160);
            b.Property(x => x.ReportAvailability).HasMaxLength(200);
            b.Property(x => x.SnapshotCanonical).HasColumnType("text");
            b.Property(x => x.SnapshotSha256).HasMaxLength(64);
            b.Property(x => x.Fingerprint).HasMaxLength(64);
            b.HasAlternateKey(x => new { x.Id, x.ProjectId });
            b.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            b.HasIndex(x => new { x.ProjectId, x.RequestKey }).IsUnique();
            b.HasOne<MeasurementChecklist>().WithMany().HasForeignKey(x => new { x.ChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MeasurementAmendment>(b =>
        {
            b.ToTable("MeasurementAmendments", t => t.HasCheckConstraint("CK_MeasurementAmendment", "(\"Version\" >= 2 AND length(btrim(\"Reason\")) > 0) AND (length(btrim(\"Reason\")) > 0) AND (length(btrim(\"ActorSubject\")) > 0) AND (length(btrim(\"Fingerprint\")) > 0)"));
            b.HasKey(x => new { x.SubmissionId, x.Version });
            b.Property(x => x.Reason).HasMaxLength(4000);
            b.Property(x => x.ActorSubject).HasMaxLength(200);
            b.Property(x => x.Fingerprint).HasMaxLength(64);
            b.HasAlternateKey(x => new { x.SubmissionId, x.Version, x.ProjectId });
            b.HasIndex(x => new { x.SubmissionId, x.RequestKey }).IsUnique();
            b.HasOne<MeasurementSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MeasurementSubmissionMember>(b =>
        {
            b.ToTable("MeasurementSubmissionMembers", t => t.HasCheckConstraint("CK_MeasurementSubmissionMember", "(\"Position\" >= 0 AND ((\"EvidenceVersion\"=1 AND \"AmendmentVersion\" IS NULL) OR (\"EvidenceVersion\">1 AND \"AmendmentVersion\"=\"EvidenceVersion\"))) AND (\"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)"));
            b.HasKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion });
            b.Property(x => x.SnapshotCanonical).HasColumnType("text");
            b.Property(x => x.SnapshotSha256).HasMaxLength(64);
            b.HasAlternateKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId });
            b.HasIndex(x => new { x.SubmissionId, x.EvidenceVersion, x.Position }).IsUnique();
            b.HasIndex(x => new { x.ProjectId, x.StructureId }).IsUnique().HasFilter("\"EvidenceVersion\" = 1");
            b.HasOne<MeasurementSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementStructure>().WithMany().HasForeignKey(x => new { x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementAmendment>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.AmendmentVersion, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.Version, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SubmissionReport>(b =>
        {
            b.ToTable("SubmissionReports", t => t.HasCheckConstraint("CK_SubmissionReport", "(\"Version\">0 AND ((\"LegacyChecklistId\" IS NOT NULL AND \"Pdf\" IS NULL) OR (\"LegacyChecklistId\" IS NULL AND octet_length(\"Pdf\")>0))) AND (\"LegacyChecklistId\" IS NOT NULL OR \"PdfSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"PdfSha256\")) > 0) AND (\"LegacyChecklistId\" IS NOT NULL OR \"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)"));
            b.Property(x => x.SnapshotCanonical).HasColumnType("text");
            b.ToTable(t => t.HasCheckConstraint("CK_SubmissionReport_Snapshot", "\"LegacyChecklistId\" IS NOT NULL OR \"SnapshotCanonical\" IS NOT NULL"));
            b.HasKey(x => new { x.SubmissionId, x.Version });
            b.Property(x => x.PdfSha256).HasMaxLength(64);
            b.Property(x => x.SnapshotSha256).HasMaxLength(64);
            b.HasOne<MeasurementSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementReport>().WithMany().HasForeignKey(x => new { x.LegacyChecklistId, x.ProjectId, x.SubmissionId }).HasPrincipalKey(x => new { x.ChecklistId, x.ProjectId, x.SubmissionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.LegacyChecklistId).IsUnique();
        });
        model.Entity<SubmissionDocumentScope>(b =>
        {
            b.ToTable("SubmissionDocumentScopes", t => t.HasCheckConstraint("CK_SubmissionDocumentScope", "(length(btrim(\"Declaration\")) > 0)"));
            b.HasKey(x => new { x.SubmissionId, x.DocumentId });
            b.Property(x => x.Declaration).HasMaxLength(4000);
            b.HasAlternateKey(x => new { x.SubmissionId, x.DocumentId, x.ProjectId });
            b.HasOne<MeasurementSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.ProjectId, x.DocumentId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SubmissionDocumentMember>(b =>
        {
            b.ToTable("SubmissionDocumentMembers");
            b.HasKey(x => new { x.SubmissionId, x.DocumentId, x.StructureId, x.EvidenceVersion });
            b.HasOne<SubmissionDocumentScope>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.DocumentId, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.DocumentId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementSubmissionMember>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringWorkspace>(b =>
        {
            b.ToTable("EngineeringWorkspaces");
            b.HasKey(x => x.ProjectId);
            b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringReceipt>(b =>
        {
            b.ToTable("EngineeringReceipts");
            b.HasKey(x => new { x.ProjectId, x.SubmissionId });
            b.HasOne<EngineeringWorkspace>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementSubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringCheckEvent>(b =>
        {
            b.ToTable("EngineeringCheckEvents");
            b.HasKey(x => new { x.ProjectId, x.RequestKey });
            b.HasIndex(x => new { x.ProjectId, x.StructureId, x.EvidenceVersion, x.Revision }).IsUnique();
            b.Property(x => x.Fingerprint).HasMaxLength(64);
            b.Property(x => x.ActorSubject).HasMaxLength(200);
            b.Property(x => x.ActorEmail).HasMaxLength(320);
            b.Property(x => x.ActorName).HasMaxLength(200);
            b.HasOne<EngineeringEvidenceCheck>().WithMany().HasForeignKey(x => new { x.ProjectId, x.SubmissionId, x.StructureId, x.EvidenceVersion }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringEvidenceCheck>(b =>
        {
            b.ToTable("EngineeringEvidenceChecks", t => t.HasCheckConstraint("CK_EngineeringEvidenceCheck", "(\"Revision\">=0 AND (NOT \"Completed\" OR (\"ChangedAt\" IS NOT NULL AND \"ChangedBySubject\" IS NOT NULL)))"));
            b.HasKey(x => new { x.ProjectId, x.SubmissionId, x.StructureId, x.EvidenceVersion });
            b.Property(x => x.ChangedBySubject).HasMaxLength(200);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<EngineeringReceipt>().WithMany().HasForeignKey(x => new { x.ProjectId, x.SubmissionId }).HasPrincipalKey(x => new { x.ProjectId, x.SubmissionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementSubmissionMember>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EngineeringStructure>().WithMany().HasForeignKey(x => new { x.LegacyChecklistId, x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.ChecklistId, x.StructureId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkPackageAssignment>(b =>
        {
            b.ToTable("WorkPackageAssignments", t => t.HasCheckConstraint("CK_WorkPackageAssignment", "(\"Area\" IN (1,4,5) AND (\"EndedAt\" IS NULL OR \"EndedAt\">=\"AssignedAt\")) AND (length(btrim(\"Subject\")) > 0)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Subject).HasMaxLength(200);
            b.HasAlternateKey(x => new { x.Id, x.ProjectId });
            b.HasIndex(x => new { x.ProjectId, x.PackageId, x.Area }).IsUnique().HasFilter("\"EndedAt\" IS NULL");
            b.HasIndex(x => x.LegacyTaskId).IsUnique();
            b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => new { x.LegacyTaskId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkPackageMember>(b =>
        {
            b.ToTable("WorkPackageMembers");
            b.HasKey(x => new { x.AssignmentId, x.StructureId });
            b.HasOne<WorkPackageAssignment>().WithMany().HasForeignKey(x => new { x.AssignmentId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementSubmissionMember>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningRecord>(b =>
        {
            b.ToTable("OpeningRecords", t => t.HasCheckConstraint("CK_OpeningRecord", "(\"Number\">0 AND \"EvidenceStatus\" IN ('COMPLETE','LEGACY_INCOMPLETE_EVIDENCE')) AND (length(btrim(\"EvidenceStatus\")) > 0)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EvidenceStatus).HasMaxLength(200);
            b.HasAlternateKey(x => new { x.Id, x.ProjectId });
            b.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            b.HasIndex(x => x.LegacyChecklistId).IsUnique();
            b.HasOne<EngineeringWorkspace>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EngineeringChecklist>().WithMany().HasForeignKey(x => new { x.LegacyChecklistId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningRecordMember>(b =>
        {
            b.ToTable("OpeningRecordMembers", t => t.HasCheckConstraint("CK_OpeningRecordMember", "(\"Position\">=0)"));
            b.HasKey(x => new { x.OpeningRecordId, x.StructureId });
            b.HasAlternateKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId });
            b.HasIndex(x => new { x.OpeningRecordId, x.Position }).IsUnique();
            b.HasOne<OpeningRecord>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MeasurementSubmissionMember>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).HasPrincipalKey(x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ActiveOpeningRecordMember>(b =>
        {
            b.ToTable("ActiveOpeningRecordMembers");
            b.HasKey(x => new { x.ProjectId, x.StructureId });
            b.HasOne<OpeningRecordMember>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningRoundLink>(b =>
        {
            b.ToTable("OpeningRoundLinks", t => t.HasCheckConstraint("CK_OpeningRoundLink", "(\"Number\">0)"));
            b.HasKey(x => x.RoundId);
            b.HasAlternateKey(x => new { x.RoundId, x.OpeningRecordId, x.ProjectId });
            b.HasIndex(x => new { x.OpeningRecordId, x.Number }).IsUnique();
            b.HasOne<OpeningRecord>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.LegacyRoundId).IsUnique();
            b.HasOne<OpeningRecordRound>().WithMany().HasForeignKey(x => new { x.LegacyRoundId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningDocumentScope>(b =>
        {
            b.ToTable("OpeningDocumentScopes", t => t.HasCheckConstraint("CK_OpeningDocumentScope", "(\"Version\">0 AND \"Kind\" IN ('ENGINEERING','REVISED','SIGNED')) AND (length(btrim(\"Kind\")) > 0) AND (\"Sha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"Sha256\")) > 0) AND (length(btrim(\"Declaration\")) > 0)"));
            b.HasKey(x => new { x.OpeningRecordId, x.DocumentId });
            b.Property(x => x.Kind).HasMaxLength(200);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.Property(x => x.Declaration).HasMaxLength(4000);
            b.HasAlternateKey(x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId });
            b.HasIndex(x => new { x.OpeningRecordId, x.Kind, x.Version }).IsUnique();
            b.HasOne<OpeningRecord>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectDocument>().WithMany().HasForeignKey(x => new { x.ProjectId, x.DocumentId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningDocumentMember>(b =>
        {
            b.ToTable("OpeningDocumentMembers");
            b.HasKey(x => new { x.OpeningRecordId, x.DocumentId, x.StructureId });
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningRecordMember>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningAcceptance>(b =>
        {
            b.ToTable("OpeningAcceptances", t => t.HasCheckConstraint("CK_OpeningAcceptance", "(\"IsLegacy\" OR (\"RequestKey\" IS NOT NULL AND \"Fingerprint\" IS NOT NULL)) AND (length(btrim(\"ActorSubject\")) > 0)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ActorSubject).HasMaxLength(200);
            b.Property(x => x.Fingerprint).HasMaxLength(64);
            b.HasAlternateKey(x => new { x.Id, x.OpeningRecordId, x.ProjectId });
            b.HasAlternateKey(x => new { x.Id, x.ProjectId });
            b.HasIndex(x => new { x.OpeningRecordId, x.RequestKey }).IsUnique();
            b.HasOne<OpeningRoundLink>().WithMany().HasForeignKey(x => new { x.RoundId, x.OpeningRecordId, x.ProjectId }).HasPrincipalKey(x => new { x.RoundId, x.OpeningRecordId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.PresentedDocumentId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.SignedDocumentId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OpeningAcceptanceMember>(b =>
        {
            b.ToTable("OpeningAcceptanceMembers");
            b.HasKey(x => new { x.AcceptanceId, x.StructureId });
            b.HasOne<OpeningAcceptance>().WithMany().HasForeignKey(x => new { x.AcceptanceId, x.OpeningRecordId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.OpeningRecordId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningRecordMember>().WithMany().HasForeignKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).HasPrincipalKey(x => new { x.OpeningRecordId, x.StructureId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.OpeningRecordId, x.StructureId }).IsUnique();
        });
        model.Entity<OpeningFilingDelivery>(b =>
        {
            b.ToTable("OpeningFilingDeliveries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.AcceptanceId).IsUnique();
            b.HasIndex(x => x.TaskId).IsUnique();
            b.HasOne<OpeningAcceptance>().WithMany().HasForeignKey(x => new { x.AcceptanceId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
