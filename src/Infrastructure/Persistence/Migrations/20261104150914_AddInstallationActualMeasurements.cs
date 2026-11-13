using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationActualMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualHeight",
                table: "MeasurementStructures",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActualSystemText",
                table: "MeasurementStructures",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualWidth",
                table: "MeasurementStructures",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                table: "MeasurementEvents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementStructures_Actual",
                table: "MeasurementStructures",
                sql: "(\"ActualWidth\" IS NULL AND \"ActualHeight\" IS NULL AND \"ActualSystemText\" IS NULL) OR (\"ActualWidth\" IS NOT NULL AND \"ActualWidth\" > 0 AND \"ActualHeight\" IS NOT NULL AND \"ActualHeight\" > 0 AND \"ActualSystemText\" IS NOT NULL AND length(btrim(\"ActualSystemText\")) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementStructures_Actual",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "ActualHeight",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "ActualSystemText",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "ActualWidth",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "DetailsJson",
                table: "MeasurementEvents");
        }
    }
}
