using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningRecordRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpeningRecordRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PresentationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentedDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PresentedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PresentedBySubject = table.Column<string>(type: "text", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedBySubject = table.Column<string>(type: "text", nullable: true),
                    GeneralObservations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReviewTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffectedDeclared = table.Column<bool>(type: "boolean", nullable: false),
                    RevisedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FilingTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewCompletedBySubject = table.Column<string>(type: "text", nullable: true),
                    FinalSnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecordRounds", x => x.Id);
                    table.UniqueConstraint("AK_OpeningRecordRounds_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_OpeningRound_Accepted", "\"Status\" <> 5 OR (\"SignedDocumentId\" IS NOT NULL AND \"FilingTaskId\" IS NOT NULL AND \"FinalSnapshotJson\" IS NOT NULL)");
                    table.CheckConstraint("CK_OpeningRound_Presentation", "(\"Status\" = 1 AND \"PresentedAt\" IS NULL AND \"PresentedBySubject\" IS NULL) OR (\"Status\" <> 1 AND \"PresentedAt\" IS NOT NULL AND \"PresentedAt\" >= \"ReceivedAt\" AND \"PresentedBySubject\" IS NOT NULL)");
                    table.CheckConstraint("CK_OpeningRound_Response", "(\"Status\" IN (1,2) AND \"RespondedAt\" IS NULL) OR (\"Status\" IN (3,4,5) AND \"RespondedAt\" IS NOT NULL AND \"RespondedAt\" >= \"PresentedAt\" AND \"RespondedBySubject\" IS NOT NULL)");
                    table.CheckConstraint("CK_OpeningRound_Reviewed", "\"Status\" NOT IN (3,4) OR (\"ReviewTaskId\" IS NOT NULL AND (\"Status\" <> 4 OR (\"RevisedDocumentId\" IS NOT NULL AND \"ReviewCompletedAt\" IS NOT NULL AND \"FinalSnapshotJson\" IS NOT NULL)))");
                    table.CheckConstraint("CK_OpeningRound_State", "\"Number\" > 0 AND \"Revision\" >= 0 AND \"Status\" IN (1,2,3,4,5)");
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_EngineeringChecklists_ChecklistId_Proje~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "EngineeringChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectDocuments_ProjectId_PresentedDoc~",
                        columns: x => new { x.ProjectId, x.PresentedDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectDocuments_ProjectId_RevisedDocum~",
                        columns: x => new { x.ProjectId, x.RevisedDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectDocuments_ProjectId_SignedDocume~",
                        columns: x => new { x.ProjectId, x.SignedDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectTasks_FilingTaskId",
                        column: x => x.FilingTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectTasks_PresentationTaskId",
                        column: x => x.PresentationTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordRounds_ProjectTasks_ReviewTaskId",
                        column: x => x.ReviewTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRecordDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffectedIdsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecordDocuments", x => x.DocumentId);
                    table.CheckConstraint("CK_OpeningDocument_State", "\"Version\" > 0 AND \"Kind\" IN (1,2)");
                    table.ForeignKey(
                        name: "FK_OpeningRecordDocuments_OpeningRecordRounds_RoundId_ProjectId",
                        columns: x => new { x.RoundId, x.ProjectId },
                        principalTable: "OpeningRecordRounds",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordDocuments_ProjectDocuments_ProjectId_DocumentId",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRecordEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecordEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningRecordEvents_OpeningRecordRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "OpeningRecordRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningRecordStructures",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Approval = table.Column<int>(type: "integer", nullable: false),
                    ApprovalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarriedApproval = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Affected = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningRecordStructures", x => new { x.RoundId, x.StructureId });
                    table.CheckConstraint("CK_OpeningStructure_State", "\"Revision\" >= 0 AND \"Approval\" IN (1,2,3) AND (\"Approval\" <> 2 OR \"ApprovalDocumentId\" IS NOT NULL) AND (\"Approval\" <> 3 OR \"ChangeDescription\" IS NOT NULL) AND (NOT \"ReviewCompleted\" OR \"Affected\")");
                    table.ForeignKey(
                        name: "FK_OpeningRecordStructures_MeasurementStructures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "MeasurementStructures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordStructures_OpeningRecordRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "OpeningRecordRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningRecordStructures_ProjectDocuments_ApprovalDocumentId",
                        column: x => x.ApprovalDocumentId,
                        principalTable: "ProjectDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_OpeningRounds",
                table: "ProjectTasks",
                sql: "\"Type\" NOT IN (8,9,10) OR (\"QuotationRound\" IS NULL AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL AND ((\"Type\" = 8 AND \"Area\" = 1) OR (\"Type\" IN (9,10) AND \"Area\" = 5)))");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordDocuments_ProjectId_DocumentId",
                table: "OpeningRecordDocuments",
                columns: new[] { "ProjectId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordDocuments_ProjectId_Kind_Version",
                table: "OpeningRecordDocuments",
                columns: new[] { "ProjectId", "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordDocuments_RoundId_ProjectId",
                table: "OpeningRecordDocuments",
                columns: new[] { "RoundId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordEvents_RoundId_RequestKey",
                table: "OpeningRecordEvents",
                columns: new[] { "RoundId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordEvents_RoundId_Revision",
                table: "OpeningRecordEvents",
                columns: new[] { "RoundId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ChecklistId_ProjectId",
                table: "OpeningRecordRounds",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_FilingTaskId",
                table: "OpeningRecordRounds",
                column: "FilingTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_PresentationTaskId",
                table: "OpeningRecordRounds",
                column: "PresentationTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ProjectId_Number",
                table: "OpeningRecordRounds",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ProjectId_PresentedDocumentId",
                table: "OpeningRecordRounds",
                columns: new[] { "ProjectId", "PresentedDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ProjectId_RevisedDocumentId",
                table: "OpeningRecordRounds",
                columns: new[] { "ProjectId", "RevisedDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ProjectId_SignedDocumentId",
                table: "OpeningRecordRounds",
                columns: new[] { "ProjectId", "SignedDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordRounds_ReviewTaskId",
                table: "OpeningRecordRounds",
                column: "ReviewTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OpeningRounds_Accepted",
                table: "OpeningRecordRounds",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" = 5");

            migrationBuilder.CreateIndex(
                name: "UX_OpeningRounds_Active",
                table: "OpeningRecordRounds",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" IN (1,2,3)");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordStructures_ApprovalDocumentId",
                table: "OpeningRecordStructures",
                column: "ApprovalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningRecordStructures_StructureId",
                table: "OpeningRecordStructures",
                column: "StructureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpeningRecordDocuments");

            migrationBuilder.DropTable(
                name: "OpeningRecordEvents");

            migrationBuilder.DropTable(
                name: "OpeningRecordStructures");

            migrationBuilder.DropTable(
                name: "OpeningRecordRounds");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_FileOpeningRecord",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_OpeningRounds",
                table: "ProjectTasks");
        }
    }
}
