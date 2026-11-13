using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComparisonJson",
                table: "MeasurementStructures",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationJson",
                table: "MeasurementStructures",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentChangeJson",
                table: "MeasurementEvents",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComparisonJson",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "ModificationJson",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "AssessmentChangeJson",
                table: "MeasurementEvents");
        }
    }
}
