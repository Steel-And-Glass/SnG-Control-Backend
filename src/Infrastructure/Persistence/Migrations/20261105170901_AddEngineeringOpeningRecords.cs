using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineeringOpeningRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEngineeringDocument",
                table: "ProjectDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EngineeringChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeasurementChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CurrentDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBySubject = table.Column<string>(type: "text", nullable: true),
                    FinalSnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringChecklists", x => x.Id);
                    table.UniqueConstraint("AK_EngineeringChecklists_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_EngineeringChecklist_State", " \"Revision\" >= 0 AND ((\"CompletedAt\" IS NULL AND \"CompletedBySubject\" IS NULL AND \"FinalSnapshotJson\" IS NULL) OR (\"CompletedAt\" >= \"CreatedAt\" AND \"CompletedBySubject\" IS NOT NULL AND \"FinalSnapshotJson\" IS NOT NULL AND \"CurrentDocumentId\" IS NOT NULL)) ");
                    table.ForeignKey(
                        name: "FK_EngineeringChecklists_MeasurementChecklists_MeasurementChec~",
                        columns: x => new { x.MeasurementChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringChecklists_ProjectDocuments_ProjectId_CurrentDoc~",
                        columns: x => new { x.ProjectId, x.CurrentDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringChecklists_ProjectTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringActas",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringActas", x => x.DocumentId);
                    table.CheckConstraint("CK_EngineeringActa_Version", " \"Version\" > 0 ");
                    table.ForeignKey(
                        name: "FK_EngineeringActas_EngineeringChecklists_ChecklistId_ProjectId",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "EngineeringChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringActas_ProjectDocuments_ProjectId_DocumentId",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "text", nullable: false),
                    ActorEmail = table.Column<string>(type: "text", nullable: true),
                    ActorName = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineeringEvents_EngineeringChecklists_ChecklistId_Project~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "EngineeringChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringStructures",
                columns: table => new
                {
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangedBySubject = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringStructures", x => new { x.ChecklistId, x.StructureId });
                    table.CheckConstraint("CK_EngineeringStructure_State", " \"Position\" >= 0 AND \"Revision\" >= 0 AND (NOT \"Completed\" OR (\"ChangedAt\" IS NOT NULL AND \"ChangedBySubject\" IS NOT NULL)) ");
                    table.ForeignKey(
                        name: "FK_EngineeringStructures_EngineeringChecklists_ChecklistId_Pro~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "EngineeringChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringStructures_MeasurementStructures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "MeasurementStructures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_Engineering",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: " \"Type\" = 7 ");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_Engineering",
                table: "ProjectTasks",
                sql: " \"Type\" <> 7 OR (\"QuotationRound\" IS NULL AND \"Area\" = 5 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL) ");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringActas_ChecklistId_ProjectId",
                table: "EngineeringActas",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringActas_ChecklistId_Version",
                table: "EngineeringActas",
                columns: new[] { "ChecklistId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringActas_ProjectId_DocumentId",
                table: "EngineeringActas",
                columns: new[] { "ProjectId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringChecklists_MeasurementChecklistId_ProjectId",
                table: "EngineeringChecklists",
                columns: new[] { "MeasurementChecklistId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringChecklists_ProjectId",
                table: "EngineeringChecklists",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringChecklists_ProjectId_CurrentDocumentId",
                table: "EngineeringChecklists",
                columns: new[] { "ProjectId", "CurrentDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringChecklists_SubmissionId",
                table: "EngineeringChecklists",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringChecklists_TaskId",
                table: "EngineeringChecklists",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringEvents_ChecklistId_ProjectId",
                table: "EngineeringEvents",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringEvents_ChecklistId_RequestKey",
                table: "EngineeringEvents",
                columns: new[] { "ChecklistId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringEvents_ChecklistId_Revision",
                table: "EngineeringEvents",
                columns: new[] { "ChecklistId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringStructures_ChecklistId_Position",
                table: "EngineeringStructures",
                columns: new[] { "ChecklistId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringStructures_ChecklistId_ProjectId",
                table: "EngineeringStructures",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringStructures_StructureId",
                table: "EngineeringStructures",
                column: "StructureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngineeringActas");

            migrationBuilder.DropTable(
                name: "EngineeringEvents");

            migrationBuilder.DropTable(
                name: "EngineeringStructures");

            migrationBuilder.DropTable(
                name: "EngineeringChecklists");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_Engineering",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_Engineering",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "IsEngineeringDocument",
                table: "ProjectDocuments");
        }
    }
}
