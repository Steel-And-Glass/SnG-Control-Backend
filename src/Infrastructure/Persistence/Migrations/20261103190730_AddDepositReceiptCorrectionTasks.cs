using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositReceiptCorrectionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionTaskId",
                table: "DepositVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ActiveDepositCorrection",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 6 AND \"Status\" IN (1,2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_DepositCorrection",
                table: "ProjectTasks",
                sql: "\"Type\" <> 6 OR (\"QuotationRound\" IS NULL AND \"Area\" = 1 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DepositVerifications_CorrectionTaskId",
                table: "DepositVerifications",
                column: "CorrectionTaskId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DepositVerifications_ProjectTasks_CorrectionTaskId",
                table: "DepositVerifications",
                column: "CorrectionTaskId",
                principalTable: "ProjectTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DO $$ BEGIN IF EXISTS (SELECT 1 FROM ""ProjectTasks"" WHERE ""Type"" = 6) THEN RAISE EXCEPTION 'Receipt correction history exists; downgrade refused'; END IF; END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_DepositVerifications_ProjectTasks_CorrectionTaskId",
                table: "DepositVerifications");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ActiveDepositCorrection",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_DepositCorrection",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_DepositVerifications_CorrectionTaskId",
                table: "DepositVerifications");

            migrationBuilder.DropColumn(
                name: "CorrectionTaskId",
                table: "DepositVerifications");
        }
    }
}
