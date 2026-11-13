using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniquePrepareQuotationTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks");
        }
    }
}
