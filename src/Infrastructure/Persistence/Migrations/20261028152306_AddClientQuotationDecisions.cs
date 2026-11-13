using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientQuotationDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientQuotationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresentQuotationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    RecordedByEmail = table.Column<string>(type: "text", nullable: true),
                    RecordedByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientQuotationDecisions", x => x.Id);
                    table.CheckConstraint("CK_ClientQuotationDecisions_Decision", "\"Decision\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_ClientQuotationDecisions_Subject", "length(btrim(\"RecordedByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_ClientQuotationDecisions_CommercialQuotations_CommercialQuo~",
                        column: x => x.CommercialQuotationId,
                        principalTable: "CommercialQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientQuotationDecisions_ProjectTasks_PresentQuotationTaskId",
                        column: x => x.PresentQuotationTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientQuotationDecisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientQuotationDecisions_ProjectId",
                table: "ClientQuotationDecisions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UX_ClientQuotationDecisions_CommercialQuotationId",
                table: "ClientQuotationDecisions",
                column: "CommercialQuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ClientQuotationDecisions_PresentQuotationTaskId",
                table: "ClientQuotationDecisions",
                column: "PresentQuotationTaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientQuotationDecisions");
        }
    }
}
