using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositVerificationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaymentReceipt",
                table: "ProjectDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DepositVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingHandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByGoogleSubject = table.Column<string>(type: "text", nullable: true),
                    DecidedByEmail = table.Column<string>(type: "text", nullable: true),
                    DecidedByName = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositVerifications", x => x.Id);
                    table.UniqueConstraint("AK_DepositVerifications_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_DepositVerifications_State", "(\"Status\" = 1 AND \"DecidedAt\" IS NULL AND \"DecidedByGoogleSubject\" IS NULL AND \"Note\" IS NULL) OR (\"Status\" IN (2,3) AND \"DecidedAt\" IS NOT NULL AND \"DecidedByGoogleSubject\" IS NOT NULL AND \"DecidedAt\" >= \"SubmittedAt\" AND length(btrim(\"DecidedByGoogleSubject\")) > 0 AND (\"Status\" <> 3 OR (\"Note\" IS NOT NULL AND length(btrim(\"Note\")) > 0)))");
                    table.CheckConstraint("CK_DepositVerifications_Submitter", "length(btrim(\"SubmittedByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_DepositVerifications_AccountingHandoffs_AccountingHandoffId",
                        column: x => x.AccountingHandoffId,
                        principalTable: "AccountingHandoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepositVerifications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepositVerificationDocuments",
                columns: table => new
                {
                    DepositVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositVerificationDocuments", x => new { x.DepositVerificationId, x.ProjectDocumentId });
                    table.ForeignKey(
                        name: "FK_DepositVerificationDocuments_DepositVerifications_DepositVe~",
                        columns: x => new { x.DepositVerificationId, x.ProjectId },
                        principalTable: "DepositVerifications",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepositVerificationDocuments_ProjectDocuments_ProjectId_Pro~",
                        columns: x => new { x.ProjectId, x.ProjectDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_Measurements",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_Installation",
                table: "ProjectTasks",
                sql: "\"Type\" <> 5 OR (\"QuotationRound\" IS NULL AND \"Area\" = 4 AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications",
                sql: "\"Kind\" IN (1, 2, 3, 4, 5, 6, 7, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_DepositVerificationDocuments_DepositVerificationId_ProjectId",
                table: "DepositVerificationDocuments",
                columns: new[] { "DepositVerificationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DepositVerificationDocuments_ProjectId_ProjectDocumentId",
                table: "DepositVerificationDocuments",
                columns: new[] { "ProjectId", "ProjectDocumentId" });

            migrationBuilder.CreateIndex(
                name: "UX_DepositVerifications_Approved",
                table: "DepositVerifications",
                column: "AccountingHandoffId",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "UX_DepositVerifications_Pending",
                table: "DepositVerifications",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_DepositVerifications_Request",
                table: "DepositVerifications",
                columns: new[] { "ProjectId", "RequestKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM ""DepositVerifications"") OR EXISTS (SELECT 1 FROM ""ProjectDocuments"" WHERE ""IsPaymentReceipt"")
                    OR EXISTS (SELECT 1 FROM ""ProjectTasks"" WHERE ""Type"" = 5) THEN
                    RAISE EXCEPTION 'Deposit verification evidence exists; downgrade would destroy history';
                END IF;
            END $$;");
            migrationBuilder.DropTable(
                name: "DepositVerificationDocuments");

            migrationBuilder.DropTable(
                name: "DepositVerifications");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_Measurements",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_Installation",
                table: "ProjectTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications");

            migrationBuilder.DropColumn(
                name: "IsPaymentReceipt",
                table: "ProjectDocuments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications",
                sql: "\"Kind\" IN (1, 2, 3, 4, 5)");
        }
    }
}
