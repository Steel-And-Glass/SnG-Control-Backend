using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialQuotationHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedFileStorageKey",
                table: "BudgetQuotationVersions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetQuotationVersions_ConfirmedFileStorageKey",
                table: "BudgetQuotationVersions",
                column: "ConfirmedFileStorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_BudgetQuotationVersions_ConfirmedFileStorageKey",
                table: "BudgetQuotationVersions");

            migrationBuilder.DropColumn(
                name: "ConfirmedFileStorageKey",
                table: "BudgetQuotationVersions");
        }
    }
}
