using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeakTimesAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeadlineAt",
                table: "ProjectTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasOverdue",
                table: "ProjectTasks",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectNotifications", x => x.Id);
                    table.CheckConstraint("CK_ProjectNotifications_Kind", "\"Kind\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_ProjectNotifications_State", "(\"Kind\" <> 4 AND \"State\" = 1 AND \"ResolvedAt\" IS NULL) OR (\"Kind\" = 4 AND ((\"State\" = 2 AND \"ResolvedAt\" IS NULL) OR (\"State\" = 3 AND \"ResolvedAt\" IS NOT NULL)))");
                    table.CheckConstraint("CK_ProjectNotifications_Text", "length(btrim(\"RecipientGoogleSubject\")) > 0 AND length(btrim(\"Title\")) > 0 AND length(btrim(\"Message\")) > 0 AND length(btrim(\"EventKey\")) > 0");
                    table.ForeignKey(
                        name: "FK_ProjectNotifications_ProjectTasks_ProjectTaskId",
                        column: x => x.ProjectTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectNotifications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNotifications_ProjectId_CreatedAt",
                table: "ProjectNotifications",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNotifications_ProjectTaskId",
                table: "ProjectNotifications",
                column: "ProjectTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNotifications_RecipientGoogleSubject_CreatedAt",
                table: "ProjectNotifications",
                columns: new[] { "RecipientGoogleSubject", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectNotifications_EventKey",
                table: "ProjectNotifications",
                column: "EventKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectNotifications");

            migrationBuilder.DropColumn(
                name: "DeadlineAt",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "WasOverdue",
                table: "ProjectTasks");
        }
    }
}
