using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingInitialDeposit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceClientQuotationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuotationRound = table.Column<int>(type: "integer", nullable: false),
                    RegisterInitialDepositTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    SentByEmail = table.Column<string>(type: "text", nullable: true),
                    SentByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingHandoffs", x => x.Id);
                    table.CheckConstraint("CK_AccountingHandoffs_Actor", "length(btrim(\"SentByGoogleSubject\")) > 0");
                    table.CheckConstraint("CK_AccountingHandoffs_Round", "\"QuotationRound\" > 0");
                    table.ForeignKey(
                        name: "FK_AccountingHandoffs_ClientQuotationDecisions_SourceClientQuo~",
                        column: x => x.SourceClientQuotationDecisionId,
                        principalTable: "ClientQuotationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingHandoffs_CommercialQuotations_CommercialQuotation~",
                        column: x => x.CommercialQuotationId,
                        principalTable: "CommercialQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingHandoffs_ProjectTasks_RegisterInitialDepositTaskId",
                        column: x => x.RegisterInitialDepositTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingHandoffs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitialDeposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingHandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterInitialDepositTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    RecordedByEmail = table.Column<string>(type: "text", nullable: true),
                    RecordedByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialDeposits", x => x.Id);
                    table.CheckConstraint("CK_InitialDeposits_Actor", "length(btrim(\"RecordedByGoogleSubject\")) > 0");
                    table.CheckConstraint("CK_InitialDeposits_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_InitialDeposits_Receipt", "\"ReceivedAt\" <= \"RecordedAt\"");
                    table.ForeignKey(
                        name: "FK_InitialDeposits_AccountingHandoffs_AccountingHandoffId",
                        column: x => x.AccountingHandoffId,
                        principalTable: "AccountingHandoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialDeposits_CommercialQuotations_CommercialQuotationId",
                        column: x => x.CommercialQuotationId,
                        principalTable: "CommercialQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialDeposits_ProjectTasks_RegisterInitialDepositTaskId",
                        column: x => x.RegisterInitialDepositTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialDeposits_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_Accounting",
                table: "ProjectTasks",
                sql: "\"Type\" <> 4 OR (\"QuotationRound\" IS NULL AND \"Area\" = 3 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingHandoffs_ProjectId",
                table: "AccountingHandoffs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountingHandoffs_Decision",
                table: "AccountingHandoffs",
                column: "SourceClientQuotationDecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountingHandoffs_Quotation",
                table: "AccountingHandoffs",
                column: "CommercialQuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountingHandoffs_Task",
                table: "AccountingHandoffs",
                column: "RegisterInitialDepositTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialDeposits_ProjectId",
                table: "InitialDeposits",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_InitialDeposits_Handoff",
                table: "InitialDeposits",
                column: "AccountingHandoffId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InitialDeposits_Quotation",
                table: "InitialDeposits",
                column: "CommercialQuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InitialDeposits_Task",
                table: "InitialDeposits",
                column: "RegisterInitialDepositTaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM ""AccountingHandoffs"") OR EXISTS (SELECT 1 FROM ""InitialDeposits"")
                    OR EXISTS (SELECT 1 FROM ""ProjectTasks"" WHERE ""Type"" = 4) THEN
                    RAISE EXCEPTION 'Accounting data exists; downgrade would destroy financial history';
                END IF;
            END $$;");
            migrationBuilder.DropTable(
                name: "InitialDeposits");

            migrationBuilder.DropTable(
                name: "AccountingHandoffs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_Accounting",
                table: "ProjectTasks");
        }
    }
}
