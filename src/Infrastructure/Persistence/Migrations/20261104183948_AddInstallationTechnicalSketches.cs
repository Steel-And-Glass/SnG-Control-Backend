using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationTechnicalSketches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SketchVersionId",
                table: "MeasurementStructures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TechnicalJson",
                table: "MeasurementStructures",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SketchChangeJson",
                table: "MeasurementEvents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TechnicalChangeJson",
                table: "MeasurementEvents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MeasurementSketchVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DrawingJson = table.Column<string>(type: "jsonb", nullable: false),
                    WebpSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementSketchVersions", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementSketchVersions_Id_StructureId", x => new { x.Id, x.StructureId });
                    table.ForeignKey(
                        name: "FK_MeasurementSketchVersions_MeasurementChecklists_ChecklistId~",
                        columns: x => new { x.ChecklistId, x.ProjectId },
                        principalTable: "MeasurementChecklists",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSketchVersions_MeasurementStructures_StructureId~",
                        columns: x => new { x.StructureId, x.ChecklistId },
                        principalTable: "MeasurementStructures",
                        principalColumns: new[] { "Id", "ChecklistId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSketchVersions_ChecklistId_ProjectId",
                table: "MeasurementSketchVersions",
                columns: new[] { "ChecklistId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSketchVersions_StructureId_ChecklistId",
                table: "MeasurementSketchVersions",
                columns: new[] { "StructureId", "ChecklistId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSketchVersions_StructureId_Revision",
                table: "MeasurementSketchVersions",
                columns: new[] { "StructureId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasurementSketchVersions");

            migrationBuilder.DropColumn(
                name: "SketchVersionId",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "TechnicalJson",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "SketchChangeJson",
                table: "MeasurementEvents");

            migrationBuilder.DropColumn(
                name: "TechnicalChangeJson",
                table: "MeasurementEvents");
        }
    }
}
