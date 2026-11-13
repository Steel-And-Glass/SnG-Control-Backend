using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDocumentsAndHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications");

            migrationBuilder.CreateTable(
                name: "ProjectDocumentHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    SentByEmail = table.Column<string>(type: "text", nullable: true),
                    SentByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocumentHandoffs", x => x.Id);
                    table.UniqueConstraint("AK_ProjectDocumentHandoffs_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.CheckConstraint("CK_ProjectDocumentHandoffs_Subject", "length(btrim(\"SentByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_ProjectDocumentHandoffs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    UploadedByEmail = table.Column<string>(type: "text", nullable: true),
                    UploadedByName = table.Column<string>(type: "text", nullable: true),
                    UploadBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocuments", x => x.Id);
                    table.UniqueConstraint("AK_ProjectDocuments_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.CheckConstraint("CK_ProjectDocuments_Size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 100000000");
                    table.CheckConstraint("CK_ProjectDocuments_State", "((\"ReplacedAt\" IS NULL AND \"ReplacedByDocumentId\" IS NULL) OR (\"ReplacedAt\" IS NOT NULL AND \"ReplacedAt\" >= \"UploadedAt\" AND \"ReplacedByDocumentId\" IS NOT NULL AND \"ReplacedByDocumentId\" <> \"Id\" AND \"RemovedAt\" IS NULL)) AND (\"RemovedAt\" IS NULL OR (\"RemovedAt\" >= \"UploadedAt\" AND \"ReplacedAt\" IS NULL))");
                    table.CheckConstraint("CK_ProjectDocuments_Text", "length(btrim(\"OriginalFileName\")) > 0 AND length(btrim(\"StorageKey\")) > 0 AND length(btrim(\"UploadedByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_ProjectDocuments_ProjectId_ReplacedByDocum~",
                        columns: x => new { x.ProjectId, x.ReplacedByDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocumentHandoffItems",
                columns: table => new
                {
                    HandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocumentHandoffItems", x => new { x.HandoffId, x.ProjectDocumentId });
                    table.ForeignKey(
                        name: "FK_ProjectDocumentHandoffItems_ProjectDocumentHandoffs_Project~",
                        columns: x => new { x.ProjectId, x.HandoffId },
                        principalTable: "ProjectDocumentHandoffs",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDocumentHandoffItems_ProjectDocuments_ProjectId_Proj~",
                        columns: x => new { x.ProjectId, x.ProjectDocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications",
                sql: "\"Kind\" IN (1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentHandoffItems_ProjectId_HandoffId",
                table: "ProjectDocumentHandoffItems",
                columns: new[] { "ProjectId", "HandoffId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentHandoffItems_ProjectId_ProjectDocumentId",
                table: "ProjectDocumentHandoffItems",
                columns: new[] { "ProjectId", "ProjectDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentHandoffs_ProjectId",
                table: "ProjectDocumentHandoffs",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_Current",
                table: "ProjectDocuments",
                column: "ProjectId",
                filter: "\"ReplacedAt\" IS NULL AND \"RemovedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_ReplacedByDocumentId",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "ReplacedByDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_UploadedAt",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_StorageKey",
                table: "ProjectDocuments",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectDocumentHandoffItems");

            migrationBuilder.DropTable(
                name: "ProjectDocumentHandoffs");

            migrationBuilder.DropTable(
                name: "ProjectDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectNotifications_Kind",
                table: "ProjectNotifications",
                sql: "\"Kind\" IN (1, 2, 3, 4)");
        }
    }
}
