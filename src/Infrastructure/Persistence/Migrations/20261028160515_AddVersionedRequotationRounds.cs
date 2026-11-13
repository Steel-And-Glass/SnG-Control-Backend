using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedRequotationRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_CommercialQuotations_BudgetQuotationVersionId",
                table: "CommercialQuotations");

            migrationBuilder.DropIndex(
                name: "IX_CommercialQuotations_CommercialCatalogId",
                table: "CommercialQuotations");

            migrationBuilder.DropIndex(
                name: "UX_CommercialQuotations_ProjectId",
                table: "CommercialQuotations");

            migrationBuilder.AddColumn<int>(
                name: "QuotationRound",
                table: "ProjectTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"ProjectTasks\" SET \"QuotationRound\" = 1 WHERE \"Type\" IN (1, 2, 3)");

            migrationBuilder.CreateTable(
                name: "RequotationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceClientQuotationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetQuotationRound = table.Column<int>(type: "integer", nullable: false),
                    PrepareQuotationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedChanges = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    RequestedByEmail = table.Column<string>(type: "text", nullable: true),
                    RequestedByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequotationRequests", x => x.Id);
                    table.CheckConstraint("CK_RequotationRequests_Changes", "length(btrim(\"RequestedChanges\")) > 0");
                    table.CheckConstraint("CK_RequotationRequests_Round", "\"TargetQuotationRound\" > 1");
                    table.CheckConstraint("CK_RequotationRequests_Subject", "length(btrim(\"RequestedByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_RequotationRequests_ClientQuotationDecisions_SourceClientQu~",
                        column: x => x.SourceClientQuotationDecisionId,
                        principalTable: "ClientQuotationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequotationRequests_ProjectTasks_PrepareQuotationTaskId",
                        column: x => x.PrepareQuotationTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequotationRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks",
                columns: new[] { "ProjectId", "QuotationRound" },
                unique: true,
                filter: "\"Type\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks",
                columns: new[] { "ProjectId", "QuotationRound" },
                unique: true,
                filter: "\"Type\" = 3");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks",
                columns: new[] { "ProjectId", "QuotationRound" },
                unique: true,
                filter: "\"Type\" = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_QuotationRound",
                table: "ProjectTasks",
                sql: "\"Type\" NOT IN (1,2,3) OR (\"QuotationRound\" IS NOT NULL AND \"QuotationRound\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_ProjectId",
                table: "CommercialQuotations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_CommercialQuotations_BudgetQuotationVersionId",
                table: "CommercialQuotations",
                column: "BudgetQuotationVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CommercialQuotations_CommercialCatalogId",
                table: "CommercialQuotations",
                column: "CommercialCatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RequotationRequests_PrepareTask",
                table: "RequotationRequests",
                column: "PrepareQuotationTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RequotationRequests_ProjectRound",
                table: "RequotationRequests",
                columns: new[] { "ProjectId", "TargetQuotationRound" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RequotationRequests_SourceDecision",
                table: "RequotationRequests",
                column: "SourceClientQuotationDecisionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ProjectTasks\" WHERE \"QuotationRound\" > 1) OR EXISTS (SELECT 1 FROM \"RequotationRequests\") THEN RAISE EXCEPTION 'Cannot remove versioning while later rounds exist'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "RequotationRequests");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_QuotationRound",
                table: "ProjectTasks");

            migrationBuilder.DropIndex(
                name: "IX_CommercialQuotations_ProjectId",
                table: "CommercialQuotations");

            migrationBuilder.DropIndex(
                name: "UX_CommercialQuotations_BudgetQuotationVersionId",
                table: "CommercialQuotations");

            migrationBuilder.DropIndex(
                name: "UX_CommercialQuotations_CommercialCatalogId",
                table: "CommercialQuotations");

            migrationBuilder.DropColumn(
                name: "QuotationRound",
                table: "ProjectTasks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PrepareQuotation",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 3");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_ReviewQuotation",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_BudgetQuotationVersionId",
                table: "CommercialQuotations",
                column: "BudgetQuotationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_CommercialCatalogId",
                table: "CommercialQuotations",
                column: "CommercialCatalogId");

            migrationBuilder.CreateIndex(
                name: "UX_CommercialQuotations_ProjectId",
                table: "CommercialQuotations",
                column: "ProjectId",
                unique: true);
        }
    }
}
