using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandInstallationSiteDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualLocation",
                table: "MeasurementStructures",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallationObservations",
                table: "MeasurementStructures",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementType",
                table: "MeasurementStructures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualLocation",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "InstallationObservations",
                table: "MeasurementStructures");

            migrationBuilder.DropColumn(
                name: "MeasurementType",
                table: "MeasurementStructures");
        }
    }
}
