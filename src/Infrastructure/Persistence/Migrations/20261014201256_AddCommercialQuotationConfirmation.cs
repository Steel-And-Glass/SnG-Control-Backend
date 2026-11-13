using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialQuotationConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialQuotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetQuotationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    FileStorageKey = table.Column<string>(type: "text", nullable: false),
                    ProjectTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedByGoogleSubject = table.Column<string>(type: "text", nullable: false),
                    ConfirmedByEmail = table.Column<string>(type: "text", nullable: true),
                    ConfirmedByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialQuotations", x => x.Id);
                    table.CheckConstraint("CK_CommercialQuotations_ProjectTotal", "\"ProjectTotal\" >= 0");
                    table.CheckConstraint("CK_CommercialQuotations_Subject", "length(btrim(\"ConfirmedByGoogleSubject\")) > 0");
                    table.ForeignKey(
                        name: "FK_CommercialQuotations_BudgetQuotationVersions_BudgetQuotatio~",
                        column: x => x.BudgetQuotationVersionId,
                        principalTable: "BudgetQuotationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialQuotations_CommercialCatalogs_CommercialCatalogId",
                        column: x => x.CommercialCatalogId,
                        principalTable: "CommercialCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialQuotations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialQuotationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialQuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Section = table.Column<int>(type: "integer", nullable: false),
                    SourceRow = table.Column<int>(type: "integer", nullable: false),
                    ItemNumber = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    System = table.Column<string>(type: "text", nullable: false),
                    Glass = table.Column<string>(type: "text", nullable: false),
                    AluminumFinish = table.Column<string>(type: "text", nullable: false),
                    LockType = table.Column<string>(type: "text", nullable: false),
                    Observations = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<decimal>(type: "numeric", nullable: false),
                    Height = table.Column<decimal>(type: "numeric", nullable: false),
                    SquareMeters = table.Column<decimal>(type: "numeric", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialQuotationItems", x => x.Id);
                    table.CheckConstraint("CK_CommercialQuotationItems_Prices", "\"UnitPrice\" >= 0 AND \"TotalPrice\" >= 0");
                    table.CheckConstraint("CK_CommercialQuotationItems_Structure", "\"SourceRow\" > 0 AND \"Section\" IN (1, 2)");
                    table.CheckConstraint("CK_CommercialQuotationItems_Values", "\"Width\" > 0 AND \"Height\" > 0 AND \"SquareMeters\" > 0 AND \"Quantity\" > 0 AND length(btrim(\"ItemNumber\")) > 0");
                    table.ForeignKey(
                        name: "FK_CommercialQuotationItems_CommercialCatalogItems_CommercialC~",
                        column: x => x.CommercialCatalogItemId,
                        principalTable: "CommercialCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialQuotationItems_CommercialQuotations_CommercialQuo~",
                        column: x => x.CommercialQuotationId,
                        principalTable: "CommercialQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks",
                column: "ProjectId",
                unique: true,
                filter: "\"Type\" = 3");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotationItems_CommercialCatalogItemId",
                table: "CommercialQuotationItems",
                column: "CommercialCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotationItems_CommercialQuotationId_CommercialCa~",
                table: "CommercialQuotationItems",
                columns: new[] { "CommercialQuotationId", "CommercialCatalogItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotationItems_CommercialQuotationId_Section_Sour~",
                table: "CommercialQuotationItems",
                columns: new[] { "CommercialQuotationId", "Section", "SourceRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_BudgetQuotationVersionId",
                table: "CommercialQuotations",
                column: "BudgetQuotationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_CommercialCatalogId",
                table: "CommercialQuotations",
                column: "CommercialCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialQuotations_FileStorageKey",
                table: "CommercialQuotations",
                column: "FileStorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CommercialQuotations_ProjectId",
                table: "CommercialQuotations",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialQuotationItems");

            migrationBuilder.DropTable(
                name: "CommercialQuotations");

            migrationBuilder.DropIndex(
                name: "UX_ProjectTasks_ProjectId_PresentQuotationToClient",
                table: "ProjectTasks");
        }
    }
}
