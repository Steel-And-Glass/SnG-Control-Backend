using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncrementalFlowFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks");

            migrationBuilder.AddColumn<Guid>(
                name: "IncrementalAcceptanceId",
                table: "ProjectTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectTasks_Id_ProjectId",
                table: "ProjectTasks",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MeasurementStructures_Id_ProjectId",
                table: "MeasurementStructures",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MeasurementReports_ChecklistId_ProjectId_SubmissionId",
                table: "MeasurementReports",
                columns: new[] { "ChecklistId", "ProjectId", "SubmissionId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EngineeringStructures_ChecklistId_StructureId_ProjectId",
                table: "EngineeringStructures",
                columns: new[] { "ChecklistId", "StructureId", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "EngineeringWorkspaces",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringWorkspaces", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_EngineeringWorkspaces_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EvidenceStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeIssue = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ReportAvailability = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SnapshotCanonical = table.Column<string>(type: "text", nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsLegacy = table.Column<bool>(type: "boolean", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementSubmissions", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementSubmissions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_MeasurementSubmission", "(\"Number\" > 0 AND \"EvidenceStatus\" IN ('COMPLETE','LEGACY_INCOMPLETE_EVIDENCE') AND \"ReportAvailability\" IN ('PRESENT','ABSENT') AND (\"IsLegacy\" OR (\"EvidenceStatus\"='COMPLETE' AND \"RequestKey\" IS NOT NULL AND \"Fingerprint\" IS NOT NULL))) AND (length(btrim(\"SubmittedBySubject\")) > 0) AND (length(btrim(\"EvidenceStatus\")) > 0) AND (length(btrim(\"ReportAvailability\")) > 0) AND (\"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)");
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissions_MeasurementChecklists_ChecklistId_Pr~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkPackageAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Area = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LegacyTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkPackageAssignments", x => x.Id);
                    table.UniqueConstraint("AK_WorkPackageAssignments_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_WorkPackageAssignment", "(\"Area\" IN (1,4,5) AND (\"EndedAt\" IS NULL OR \"EndedAt\">=\"AssignedAt\")) AND (length(btrim(\"Subject\")) > 0)");
                    table.ForeignKey(
                        name: "FK_WorkPackageAssignments_ProjectTasks_LegacyTaskId_ProjectId",
                        columns: x => new { x.LegacyTaskId, x.ProjectId },
                        principalTable: "ProjectTasks",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkPackageAssignments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LegacyChecklistId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecords", x => x.Id);
                    table.UniqueConstraint("AK_OpeningRecords_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_OpeningRecord", "(\"Number\">0 AND \"EvidenceStatus\" IN ('COMPLETE','LEGACY_INCOMPLETE_EVIDENCE')) AND (length(btrim(\"EvidenceStatus\")) > 0)");
                    table.ForeignKey(
                        name: "FK_OpeningRecords_EngineeringChecklists_LegacyChecklistId_Proj~",
                        columns: x => new { x.LegacyChecklistId, x.ProjectId },
                        principalTable: "EngineeringChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecords_EngineeringWorkspaces_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "EngineeringWorkspaces",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringReceipts",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringReceipts", x => new { x.ProjectId, x.SubmissionId });
                    table.ForeignKey(
                        name: "FK_EngineeringReceipts_EngineeringWorkspaces_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "EngineeringWorkspaces",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringReceipts_MeasurementSubmissions_SubmissionId_Pro~",
                        columns: x => new { x.SubmissionId, x.ProjectId },
                        principalTable: "MeasurementSubmissions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementAmendments",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementAmendments", x => new { x.SubmissionId, x.Version });
                    table.UniqueConstraint("AK_MeasurementAmendments_SubmissionId_Version_ProjectId", x => new { x.SubmissionId, x.Version, x.ProjectId });
                    table.CheckConstraint("CK_MeasurementAmendment", "(\"Version\" >= 2 AND length(btrim(\"Reason\")) > 0) AND (length(btrim(\"Reason\")) > 0) AND (length(btrim(\"ActorSubject\")) > 0) AND (length(btrim(\"Fingerprint\")) > 0)");
                    table.ForeignKey(
                        name: "FK_MeasurementAmendments_MeasurementSubmissions_SubmissionId_P~",
                        columns: x => new { x.SubmissionId, x.ProjectId },
                        principalTable: "MeasurementSubmissions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionDocumentScopes",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Declaration = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionDocumentScopes", x => new { x.SubmissionId, x.DocumentId });
                    table.UniqueConstraint("AK_SubmissionDocumentScopes_SubmissionId_DocumentId_ProjectId", x => new { x.SubmissionId, x.DocumentId, x.ProjectId });
                    table.CheckConstraint("CK_SubmissionDocumentScope", "(length(btrim(\"Declaration\")) > 0)");
                    table.ForeignKey(
                        name: "FK_SubmissionDocumentScopes_MeasurementSubmissions_SubmissionI~",
                        columns: x => new { x.SubmissionId, x.ProjectId },
                        principalTable: "MeasurementSubmissions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionDocumentScopes_ProjectDocuments_ProjectId_Documen~",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionReports",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotCanonical = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyChecklistId = table.Column<Guid>(type: "uuid", nullable: true),
                    Pdf = table.Column<byte[]>(type: "bytea", nullable: true),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionReports", x => new { x.SubmissionId, x.Version });
                    table.CheckConstraint("CK_SubmissionReport", "(\"Version\">0 AND ((\"LegacyChecklistId\" IS NOT NULL AND \"Pdf\" IS NULL) OR (\"LegacyChecklistId\" IS NULL AND octet_length(\"Pdf\")>0))) AND (\"LegacyChecklistId\" IS NOT NULL OR \"PdfSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"PdfSha256\")) > 0) AND (\"LegacyChecklistId\" IS NOT NULL OR \"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)");
                    table.CheckConstraint("CK_SubmissionReport_Snapshot", "\"LegacyChecklistId\" IS NOT NULL OR \"SnapshotCanonical\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_SubmissionReports_MeasurementReports_LegacyChecklistId_Proj~",
                        columns: x => new { x.LegacyChecklistId, x.ProjectId, x.SubmissionId },
                        principalTable: "MeasurementReports",
                        principalColumns: new[] { "ChecklistId", "ProjectId", "SubmissionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionReports_MeasurementSubmissions_SubmissionId_Proje~",
                        columns: x => new { x.SubmissionId, x.ProjectId },
                        principalTable: "MeasurementSubmissions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningDocumentScopes",
                columns: table => new
                {
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Declaration = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningDocumentScopes", x => new { x.OpeningRecordId, x.DocumentId });
                    table.UniqueConstraint("AK_OpeningDocumentScopes_OpeningRecordId_DocumentId_ProjectId", x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId });
                    table.CheckConstraint("CK_OpeningDocumentScope", "(\"Version\">0 AND \"Kind\" IN ('ENGINEERING','REVISED','SIGNED')) AND (length(btrim(\"Kind\")) > 0) AND (\"Sha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"Sha256\")) > 0) AND (length(btrim(\"Declaration\")) > 0)");
                    table.ForeignKey(
                        name: "FK_OpeningDocumentScopes_OpeningRecords_OpeningRecordId_Projec~",
                        columns: x => new { x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRecords",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningDocumentScopes_ProjectDocuments_ProjectId_DocumentId",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRoundLinks",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyRoundId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRoundLinks", x => x.RoundId);
                    table.UniqueConstraint("AK_OpeningRoundLinks_RoundId_OpeningRecordId_ProjectId", x => new { x.RoundId, x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_OpeningRoundLink", "(\"Number\">0)");
                    table.ForeignKey(
                        name: "FK_OpeningRoundLinks_OpeningRecordRounds_LegacyRoundId_Project~",
                        columns: x => new { x.LegacyRoundId, x.ProjectId },
                        principalTable: "OpeningRecordRounds",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRoundLinks_OpeningRecords_OpeningRecordId_ProjectId",
                        columns: x => new { x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRecords",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementSubmissionMembers",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmendmentVersion = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SnapshotCanonical = table.Column<string>(type: "text", nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementSubmissionMembers", x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion });
                    table.UniqueConstraint("AK_MeasurementSubmissionMembers_SubmissionId_StructureId_Evide~", x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId });
                    table.CheckConstraint("CK_MeasurementSubmissionMember", "(\"Position\" >= 0 AND ((\"EvidenceVersion\"=1 AND \"AmendmentVersion\" IS NULL) OR (\"EvidenceVersion\">1 AND \"AmendmentVersion\"=\"EvidenceVersion\"))) AND (\"SnapshotSha256\" ~ '^[A-Fa-f0-9]{64}$') AND (length(btrim(\"SnapshotSha256\")) > 0)");
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissionMembers_MeasurementAmendments_Submissi~",
                        columns: x => new { x.SubmissionId, x.AmendmentVersion, x.ProjectId },
                        principalTable: "MeasurementAmendments",
                        principalColumns: new[] { "SubmissionId", "Version", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissionMembers_MeasurementStructures_Structur~",
                        columns: x => new { x.StructureId, x.ProjectId },
                        principalTable: "MeasurementStructures",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissionMembers_MeasurementSubmissions_Submiss~",
                        columns: x => new { x.SubmissionId, x.ProjectId },
                        principalTable: "MeasurementSubmissions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentedDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsLegacy = table.Column<bool>(type: "boolean", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningAcceptances", x => x.Id);
                    table.UniqueConstraint("AK_OpeningAcceptances_Id_OpeningRecordId_ProjectId", x => new { x.Id, x.OpeningRecordId, x.ProjectId });
                    table.UniqueConstraint("AK_OpeningAcceptances_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_OpeningAcceptance", "(\"IsLegacy\" OR (\"RequestKey\" IS NOT NULL AND \"Fingerprint\" IS NOT NULL)) AND (length(btrim(\"ActorSubject\")) > 0)");
                    table.ForeignKey(
                        name: "FK_OpeningAcceptances_OpeningDocumentScopes_OpeningRecordId_Pr~",
                        columns: x => new { x.OpeningRecordId, x.PresentedDocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningAcceptances_OpeningDocumentScopes_OpeningRecordId_Si~",
                        columns: x => new { x.OpeningRecordId, x.SignedDocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningAcceptances_OpeningRoundLinks_RoundId_OpeningRecordI~",
                        columns: x => new { x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRoundLinks",
                        principalColumns: new[] { "RoundId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringEvidenceChecks",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LegacyChecklistId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringEvidenceChecks", x => new { x.ProjectId, x.SubmissionId, x.StructureId, x.EvidenceVersion });
                    table.CheckConstraint("CK_EngineeringEvidenceCheck", "(\"Revision\">=0 AND (NOT \"Completed\" OR (\"ChangedAt\" IS NOT NULL AND \"ChangedBySubject\" IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_EngineeringEvidenceChecks_EngineeringReceipts_ProjectId_Sub~",
                        columns: x => new { x.ProjectId, x.SubmissionId },
                        principalTable: "EngineeringReceipts",
                        principalColumns: new[] { "ProjectId", "SubmissionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringEvidenceChecks_EngineeringStructures_LegacyCheck~",
                        columns: x => new { x.LegacyChecklistId, x.StructureId, x.ProjectId },
                        principalTable: "EngineeringStructures",
                        principalColumns: new[] { "ChecklistId", "StructureId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringEvidenceChecks_MeasurementSubmissionMembers_Subm~",
                        columns: x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId },
                        principalTable: "MeasurementSubmissionMembers",
                        principalColumns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRecordMembers",
                columns: table => new
                {
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecordMembers", x => new { x.OpeningRecordId, x.StructureId });
                    table.UniqueConstraint("AK_OpeningRecordMembers_OpeningRecordId_StructureId_ProjectId", x => new { x.OpeningRecordId, x.StructureId, x.ProjectId });
                    table.CheckConstraint("CK_OpeningRecordMember", "(\"Position\">=0)");
                    table.ForeignKey(
                        name: "FK_OpeningRecordMembers_MeasurementSubmissionMembers_Submissio~",
                        columns: x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId },
                        principalTable: "MeasurementSubmissionMembers",
                        principalColumns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordMembers_OpeningRecords_OpeningRecordId_Project~",
                        columns: x => new { x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRecords",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionDocumentMembers",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionDocumentMembers", x => new { x.SubmissionId, x.DocumentId, x.StructureId, x.EvidenceVersion });
                    table.ForeignKey(
                        name: "FK_SubmissionDocumentMembers_MeasurementSubmissionMembers_Subm~",
                        columns: x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId },
                        principalTable: "MeasurementSubmissionMembers",
                        principalColumns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionDocumentMembers_SubmissionDocumentScopes_Submissi~",
                        columns: x => new { x.SubmissionId, x.DocumentId, x.ProjectId },
                        principalTable: "SubmissionDocumentScopes",
                        principalColumns: new[] { "SubmissionId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkPackageMembers",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkPackageMembers", x => new { x.AssignmentId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_WorkPackageMembers_MeasurementSubmissionMembers_SubmissionI~",
                        columns: x => new { x.SubmissionId, x.StructureId, x.EvidenceVersion, x.ProjectId },
                        principalTable: "MeasurementSubmissionMembers",
                        principalColumns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkPackageMembers_WorkPackageAssignments_AssignmentId_Proj~",
                        columns: x => new { x.AssignmentId, x.ProjectId },
                        principalTable: "WorkPackageAssignments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningFilingDeliveries_OpeningAcceptances_AcceptanceId_Pro~",
                        columns: x => new { x.AcceptanceId, x.ProjectId },
                        principalTable: "OpeningAcceptances",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningFilingDeliveries_ProjectTasks_TaskId_ProjectId",
                        columns: x => new { x.TaskId, x.ProjectId },
                        principalTable: "ProjectTasks",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActiveOpeningRecordMembers",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveOpeningRecordMembers", x => new { x.ProjectId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_ActiveOpeningRecordMembers_OpeningRecordMembers_OpeningReco~",
                        columns: x => new { x.OpeningRecordId, x.StructureId, x.ProjectId },
                        principalTable: "OpeningRecordMembers",
                        principalColumns: new[] { "OpeningRecordId", "StructureId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningAcceptanceMembers",
                columns: table => new
                {
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningAcceptanceMembers", x => new { x.AcceptanceId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_OpeningAcceptanceMembers_OpeningAcceptances_AcceptanceId_Op~",
                        columns: x => new { x.AcceptanceId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningAcceptances",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningAcceptanceMembers_OpeningRecordMembers_OpeningRecord~",
                        columns: x => new { x.OpeningRecordId, x.StructureId, x.ProjectId },
                        principalTable: "OpeningRecordMembers",
                        principalColumns: new[] { "OpeningRecordId", "StructureId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningDocumentMembers",
                columns: table => new
                {
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningDocumentMembers", x => new { x.OpeningRecordId, x.DocumentId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_OpeningDocumentMembers_OpeningDocumentScopes_OpeningRecordI~",
                        columns: x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningDocumentMembers_OpeningRecordMembers_OpeningRecordId~",
                        columns: x => new { x.OpeningRecordId, x.StructureId, x.ProjectId },
                        principalTable: "OpeningRecordMembers",
                        principalColumns: new[] { "OpeningRecordId", "StructureId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTasks_IncrementalAcceptanceId",
                table: "ProjectTasks",
                column: "IncrementalAcceptanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTasks_IncrementalAcceptanceId_ProjectId",
                table: "ProjectTasks",
                columns: new[] { "IncrementalAcceptanceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 10 AND \"IncrementalAcceptanceId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_IncrementalAcceptance",
                table: "ProjectTasks",
                sql: "\"IncrementalAcceptanceId\" IS NULL OR \"Type\" = 10");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveOpeningRecordMembers_OpeningRecordId_StructureId_Proj~",
                table: "ActiveOpeningRecordMembers",
                columns: new[] { "OpeningRecordId", "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringEvidenceChecks_LegacyChecklistId_StructureId_Pro~",
                table: "EngineeringEvidenceChecks",
                columns: new[] { "LegacyChecklistId", "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringEvidenceChecks_SubmissionId_StructureId_Evidence~",
                table: "EngineeringEvidenceChecks",
                columns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringReceipts_SubmissionId_ProjectId",
                table: "EngineeringReceipts",
                columns: new[] { "SubmissionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementAmendments_SubmissionId_ProjectId",
                table: "MeasurementAmendments",
                columns: new[] { "SubmissionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementAmendments_SubmissionId_RequestKey",
                table: "MeasurementAmendments",
                columns: new[] { "SubmissionId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionMembers_ProjectId_StructureId",
                table: "MeasurementSubmissionMembers",
                columns: new[] { "ProjectId", "StructureId" },
                unique: true,
                filter: "\"EvidenceVersion\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionMembers_StructureId_ProjectId",
                table: "MeasurementSubmissionMembers",
                columns: new[] { "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionMembers_SubmissionId_AmendmentVersion_~",
                table: "MeasurementSubmissionMembers",
                columns: new[] { "SubmissionId", "AmendmentVersion", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionMembers_SubmissionId_EvidenceVersion_P~",
                table: "MeasurementSubmissionMembers",
                columns: new[] { "SubmissionId", "EvidenceVersion", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionMembers_SubmissionId_ProjectId",
                table: "MeasurementSubmissionMembers",
                columns: new[] { "SubmissionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissions_ChecklistId_ProjectId",
                table: "MeasurementSubmissions",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissions_ProjectId_Number",
                table: "MeasurementSubmissions",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissions_ProjectId_RequestKey",
                table: "MeasurementSubmissions",
                columns: new[] { "ProjectId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptanceMembers_AcceptanceId_OpeningRecordId_Proje~",
                table: "OpeningAcceptanceMembers",
                columns: new[] { "AcceptanceId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptanceMembers_OpeningRecordId_StructureId",
                table: "OpeningAcceptanceMembers",
                columns: new[] { "OpeningRecordId", "StructureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptanceMembers_OpeningRecordId_StructureId_Projec~",
                table: "OpeningAcceptanceMembers",
                columns: new[] { "OpeningRecordId", "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptances_OpeningRecordId_PresentedDocumentId_Proj~",
                table: "OpeningAcceptances",
                columns: new[] { "OpeningRecordId", "PresentedDocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptances_OpeningRecordId_RequestKey",
                table: "OpeningAcceptances",
                columns: new[] { "OpeningRecordId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptances_OpeningRecordId_SignedDocumentId_Project~",
                table: "OpeningAcceptances",
                columns: new[] { "OpeningRecordId", "SignedDocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAcceptances_RoundId_OpeningRecordId_ProjectId",
                table: "OpeningAcceptances",
                columns: new[] { "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningDocumentMembers_OpeningRecordId_DocumentId_ProjectId",
                table: "OpeningDocumentMembers",
                columns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningDocumentMembers_OpeningRecordId_StructureId_ProjectId",
                table: "OpeningDocumentMembers",
                columns: new[] { "OpeningRecordId", "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningDocumentScopes_OpeningRecordId_Kind_Version",
                table: "OpeningDocumentScopes",
                columns: new[] { "OpeningRecordId", "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningDocumentScopes_OpeningRecordId_ProjectId",
                table: "OpeningDocumentScopes",
                columns: new[] { "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningDocumentScopes_ProjectId_DocumentId",
                table: "OpeningDocumentScopes",
                columns: new[] { "ProjectId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDeliveries_AcceptanceId",
                table: "OpeningFilingDeliveries",
                column: "AcceptanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDeliveries_AcceptanceId_ProjectId",
                table: "OpeningFilingDeliveries",
                columns: new[] { "AcceptanceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDeliveries_TaskId",
                table: "OpeningFilingDeliveries",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDeliveries_TaskId_ProjectId",
                table: "OpeningFilingDeliveries",
                columns: new[] { "TaskId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordMembers_OpeningRecordId_Position",
                table: "OpeningRecordMembers",
                columns: new[] { "OpeningRecordId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordMembers_OpeningRecordId_ProjectId",
                table: "OpeningRecordMembers",
                columns: new[] { "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordMembers_SubmissionId_StructureId_EvidenceVersi~",
                table: "OpeningRecordMembers",
                columns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecords_LegacyChecklistId",
                table: "OpeningRecords",
                column: "LegacyChecklistId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecords_LegacyChecklistId_ProjectId",
                table: "OpeningRecords",
                columns: new[] { "LegacyChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecords_ProjectId_Number",
                table: "OpeningRecords",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRoundLinks_LegacyRoundId",
                table: "OpeningRoundLinks",
                column: "LegacyRoundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRoundLinks_LegacyRoundId_ProjectId",
                table: "OpeningRoundLinks",
                columns: new[] { "LegacyRoundId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRoundLinks_OpeningRecordId_Number",
                table: "OpeningRoundLinks",
                columns: new[] { "OpeningRecordId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRoundLinks_OpeningRecordId_ProjectId",
                table: "OpeningRoundLinks",
                columns: new[] { "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionDocumentMembers_SubmissionId_DocumentId_ProjectId",
                table: "SubmissionDocumentMembers",
                columns: new[] { "SubmissionId", "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionDocumentMembers_SubmissionId_StructureId_Evidence~",
                table: "SubmissionDocumentMembers",
                columns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionDocumentScopes_ProjectId_DocumentId",
                table: "SubmissionDocumentScopes",
                columns: new[] { "ProjectId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionDocumentScopes_SubmissionId_ProjectId",
                table: "SubmissionDocumentScopes",
                columns: new[] { "SubmissionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionReports_LegacyChecklistId",
                table: "SubmissionReports",
                column: "LegacyChecklistId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionReports_LegacyChecklistId_ProjectId_SubmissionId",
                table: "SubmissionReports",
                columns: new[] { "LegacyChecklistId", "ProjectId", "SubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionReports_SubmissionId_ProjectId",
                table: "SubmissionReports",
                columns: new[] { "SubmissionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageAssignments_LegacyTaskId",
                table: "WorkPackageAssignments",
                column: "LegacyTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageAssignments_LegacyTaskId_ProjectId",
                table: "WorkPackageAssignments",
                columns: new[] { "LegacyTaskId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageAssignments_ProjectId_PackageId_Area",
                table: "WorkPackageAssignments",
                columns: new[] { "ProjectId", "PackageId", "Area" },
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageMembers_AssignmentId_ProjectId",
                table: "WorkPackageMembers",
                columns: new[] { "AssignmentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageMembers_SubmissionId_StructureId_EvidenceVersion~",
                table: "WorkPackageMembers",
                columns: new[] { "SubmissionId", "StructureId", "EvidenceVersion", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectTasks_OpeningAcceptances_IncrementalAcceptanceId_Pro~",
                table: "ProjectTasks",
                columns: new[] { "IncrementalAcceptanceId", "ProjectId" },
                principalTable: "OpeningAcceptances",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
            using var stream = typeof(AddIncrementalFlowFoundation).Assembly.GetManifestResourceStream(
                "SnG.Control.Infrastructure.Persistence.Migrations.20261110152041_IncrementalFoundation.sql")
                ?? throw new InvalidOperationException("Incremental migration resource missing.");
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "MeasurementSubmissions" WHERE NOT "IsLegacy")
                    OR EXISTS (SELECT 1 FROM "MeasurementAmendments")
                    OR EXISTS (SELECT 1 FROM "OpeningRecords" WHERE "LegacyChecklistId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "OpeningAcceptances" WHERE NOT "IsLegacy")
                    OR EXISTS (SELECT 1 FROM "WorkPackageAssignments" WHERE "LegacyTaskId" IS NULL)
                  THEN RAISE EXCEPTION 'Native incremental evidence exists; use a forward migration'; END IF;
                END; $guard$;
                DROP TRIGGER incremental_task_binding ON "ProjectTasks";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectTasks_OpeningAcceptances_IncrementalAcceptanceId_Pro~",
                table: "ProjectTasks");

            migrationBuilder.DropTable(
                name: "ActiveOpeningRecordMembers");

            migrationBuilder.DropTable(
                name: "EngineeringEvidenceChecks");

            migrationBuilder.DropTable(
                name: "OpeningAcceptanceMembers");

            migrationBuilder.DropTable(
                name: "OpeningDocumentMembers");

            migrationBuilder.DropTable(
                name: "OpeningFilingDeliveries");

            migrationBuilder.DropTable(
                name: "SubmissionDocumentMembers");

            migrationBuilder.DropTable(
                name: "SubmissionReports");

            migrationBuilder.DropTable(
                name: "WorkPackageMembers");

            migrationBuilder.DropTable(
                name: "EngineeringReceipts");

            migrationBuilder.DropTable(
                name: "OpeningRecordMembers");

            migrationBuilder.DropTable(
                name: "OpeningAcceptances");

            migrationBuilder.DropTable(
                name: "SubmissionDocumentScopes");

            migrationBuilder.DropTable(
                name: "WorkPackageAssignments");

            migrationBuilder.DropTable(
                name: "MeasurementSubmissionMembers");

            migrationBuilder.DropTable(
                name: "OpeningDocumentScopes");

            migrationBuilder.DropTable(
                name: "OpeningRoundLinks");

            migrationBuilder.DropTable(
                name: "MeasurementAmendments");

            migrationBuilder.DropTable(
                name: "OpeningRecords");

            migrationBuilder.DropTable(
                name: "MeasurementSubmissions");

            migrationBuilder.DropTable(
                name: "EngineeringWorkspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectTasks_Id_ProjectId",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectTasks_IncrementalAcceptanceId",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectTasks_IncrementalAcceptanceId_ProjectId",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_IncrementalAcceptance",
                table: "ProjectTasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MeasurementStructures_Id_ProjectId",
                table: "MeasurementStructures");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MeasurementReports_ChecklistId_ProjectId_SubmissionId",
                table: "MeasurementReports");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EngineeringStructures_ChecklistId_StructureId_ProjectId",
                table: "EngineeringStructures");

            migrationBuilder.DropColumn(
                name: "IncrementalAcceptanceId",
                table: "ProjectTasks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 10");
            migrationBuilder.Sql("""
                DROP FUNCTION sng_incremental_legacy_project(uuid);
                DROP FUNCTION sng_incremental_frozen();
                DROP FUNCTION sng_incremental_complete_submission();
                DROP FUNCTION sng_incremental_snapshot_hash();
                DROP FUNCTION sng_incremental_opening_member();
                DROP FUNCTION sng_incremental_acceptance_member();
                DROP FUNCTION sng_incremental_acceptance();
                DROP FUNCTION sng_incremental_filing();
                DROP FUNCTION sng_incremental_native_members();
                DROP FUNCTION sng_incremental_scope_insert();
                DROP FUNCTION sng_incremental_report_integrity();
                DROP FUNCTION sng_incremental_task_binding();
                """);
        }
    }
}
