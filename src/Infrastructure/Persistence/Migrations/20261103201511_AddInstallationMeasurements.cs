using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMeasurementDocument",
                table: "ProjectDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MeasurementChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuotationRound = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedBySubject = table.Column<string>(type: "text", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementChecklists", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementChecklists_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_MeasurementChecklists_State", "\"Revision\" >= 0 AND \"QuotationRound\" > 0 AND ((\"SubmissionId\" IS NULL AND \"SubmittedAt\" IS NULL AND \"SubmittedBySubject\" IS NULL AND \"SnapshotJson\" IS NULL) OR (\"SubmissionId\" IS NOT NULL AND \"SubmittedAt\" >= \"CreatedAt\" AND \"SubmittedBySubject\" IS NOT NULL AND \"SnapshotJson\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_MeasurementChecklists_CommercialQuotations_CommercialQuotat~",
                        column: x => x.CommercialQuotationId,
                        principalTable: "CommercialQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementChecklists_ProjectTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementChecklists_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementStructures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitOrdinal = table.Column<int>(type: "integer", nullable: false),
                    UnitCount = table.Column<int>(type: "integer", nullable: false),
                    ItemNumber = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    Width = table.Column<decimal>(type: "numeric", nullable: false),
                    Height = table.Column<decimal>(type: "numeric", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "text", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MeasuredBySubject = table.Column<string>(type: "text", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredBySubject = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementStructures", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementStructures_Id_ChecklistId", x => new { x.Id, x.ChecklistId });
                    table.CheckConstraint("CK_MeasurementStructures_State", "((\"MeasuredAt\" IS NULL AND \"MeasuredBySubject\" IS NULL) OR (\"MeasuredAt\" >= \"CreatedAt\" AND \"MeasuredBySubject\" IS NOT NULL)) AND ((\"RetiredAt\" IS NULL AND \"RetiredBySubject\" IS NULL) OR (\"RetiredAt\" >= \"CreatedAt\" AND \"RetiredBySubject\" IS NOT NULL))");
                    table.CheckConstraint("CK_MeasurementStructures_Unit", "\"UnitOrdinal\" > 0 AND \"UnitOrdinal\" <= \"UnitCount\" AND (\"CommercialQuotationItemId\" IS NOT NULL OR (\"UnitOrdinal\" = 1 AND \"UnitCount\" = 1 AND \"Width\" > 0 AND \"Height\" > 0))");
                    table.ForeignKey(
                        name: "FK_MeasurementStructures_CommercialQuotationItems_CommercialQu~",
                        column: x => x.CommercialQuotationItemId,
                        principalTable: "CommercialQuotationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementStructures_MeasurementChecklists_ChecklistId_Pro~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementSubmissionDocuments",
                columns: table => new
                {
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementSubmissionDocuments", x => new { x.ChecklistId, x.ProjectDocumentId });
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissionDocuments_MeasurementChecklists_Checkl~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSubmissionDocuments_ProjectDocuments_ProjectId_P~",
                        columns: x => new { x.ProjectId, x.ProjectDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "text", nullable: false),
                    ActorEmail = table.Column<string>(type: "text", nullable: true),
                    ActorName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementEvents_MeasurementChecklists_ChecklistId_Project~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementEvents_MeasurementStructures_StructureId_Checkli~",
                        columns: x => new { x.StructureId, x.ChecklistId },
                        principalTable: "MeasurementStructures",
                        principalColumns: new[] { "Id", "ChecklistId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementChecklists_CommercialQuotationId",
                table: "MeasurementChecklists",
                column: "CommercialQuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementChecklists_ProjectId",
                table: "MeasurementChecklists",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementChecklists_SubmissionId",
                table: "MeasurementChecklists",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementChecklists_TaskId",
                table: "MeasurementChecklists",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementEvents_ChecklistId_ProjectId",
                table: "MeasurementEvents",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementEvents_ChecklistId_RequestKey",
                table: "MeasurementEvents",
                columns: new[] { "ChecklistId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementEvents_ChecklistId_Revision",
                table: "MeasurementEvents",
                columns: new[] { "ChecklistId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementEvents_StructureId_ChecklistId",
                table: "MeasurementEvents",
                columns: new[] { "StructureId", "ChecklistId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementStructures_ChecklistId_CommercialQuotationItemId~",
                table: "MeasurementStructures",
                columns: new[] { "ChecklistId", "CommercialQuotationItemId", "UnitOrdinal" },
                unique: true,
                filter: "\"CommercialQuotationItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementStructures_ChecklistId_ProjectId",
                table: "MeasurementStructures",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementStructures_CommercialQuotationItemId",
                table: "MeasurementStructures",
                column: "CommercialQuotationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionDocuments_ChecklistId_ProjectId",
                table: "MeasurementSubmissionDocuments",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSubmissionDocuments_ProjectId_ProjectDocumentId",
                table: "MeasurementSubmissionDocuments",
                columns: new[] { "ProjectId", "ProjectDocumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasurementEvents");

            migrationBuilder.DropTable(
                name: "MeasurementSubmissionDocuments");

            migrationBuilder.DropTable(
                name: "MeasurementStructures");

            migrationBuilder.DropTable(
                name: "MeasurementChecklists");

            migrationBuilder.DropColumn(
                name: "IsMeasurementDocument",
                table: "ProjectDocuments");
        }
    }
}
