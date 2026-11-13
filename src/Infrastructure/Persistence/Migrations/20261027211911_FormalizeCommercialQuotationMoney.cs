using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormalizeCommercialQuotationMoney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL numeric ROUND breaks midpoint ties away from zero, like Money.Normalize.
            // Normalize explicitly before changing scale; keep rows, keys and workbook bytes intact.
            migrationBuilder.Sql("""
                UPDATE "CommercialQuotations" SET "ProjectTotal" = ROUND("ProjectTotal", 2);
                UPDATE "CommercialQuotationItems"
                SET "UnitPrice" = ROUND("UnitPrice", 2), "TotalPrice" = ROUND("TotalPrice", 2);
                """);
            migrationBuilder.AlterColumn<decimal>(
                name: "ProjectTotal",
                table: "CommercialQuotations",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "CommercialQuotationItems",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalPrice",
                table: "CommercialQuotationItems",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ProjectTotal",
                table: "CommercialQuotations",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,2)",
                oldPrecision: 20,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "CommercialQuotationItems",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,2)",
                oldPrecision: 20,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalPrice",
                table: "CommercialQuotationItems",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,2)",
                oldPrecision: 20,
                oldScale: 2);
        }
    }
}
