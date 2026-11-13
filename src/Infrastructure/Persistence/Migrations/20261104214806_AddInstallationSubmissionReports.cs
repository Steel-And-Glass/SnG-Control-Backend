using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationSubmissionReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeasurementReports",
                columns: table => new
                {
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Pdf = table.Column<byte[]>(type: "bytea", nullable: false),
                    SnapshotCanonical = table.Column<string>(type: "text", nullable: false),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementReports", x => x.ChecklistId);
                    table.ForeignKey(
                        name: "FK_MeasurementReports_MeasurementChecklists_ChecklistId_Projec~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReports_ChecklistId_ProjectId",
                table: "MeasurementReports",
                columns: new[] { "ChecklistId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReports_SubmissionId",
                table: "MeasurementReports",
                column: "SubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasurementReports");
        }
    }
}
