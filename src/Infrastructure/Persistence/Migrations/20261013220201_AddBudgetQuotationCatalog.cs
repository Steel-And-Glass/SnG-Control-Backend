using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetQuotationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetQuotationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    OriginalFileStorageKey = table.Column<string>(type: "text", nullable: false),
                    OriginalFileContentType = table.Column<string>(type: "text", nullable: false),
                    DocumentClientName = table.Column<string>(type: "text", nullable: false),
                    DocumentProjectName = table.Column<string>(type: "text", nullable: false),
                    DocumentLocation = table.Column<string>(type: "text", nullable: false),
                    ClientLine = table.Column<string>(type: "text", nullable: false),
                    ProductionLine = table.Column<string>(type: "text", nullable: false),
                    PreparedBy = table.Column<string>(type: "text", nullable: false),
                    DocumentSgCode = table.Column<string>(type: "text", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetQuotationVersions", x => x.Id);
                    table.CheckConstraint("CK_BudgetQuotationVersions_VersionNumber", "\"VersionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_BudgetQuotationVersions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialCatalogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetQuotationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialCatalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialCatalogs_BudgetQuotationVersions_BudgetQuotationV~",
                        column: x => x.BudgetQuotationVersionId,
                        principalTable: "BudgetQuotationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommercialCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CommercialCatalogItems", x => x.Id);
                    table.CheckConstraint("CK_CommercialCatalogItems_Section", "\"Section\" IN (1, 2)");
                    table.CheckConstraint("CK_CommercialCatalogItems_SourceRow", "\"SourceRow\" > 0");
                    table.CheckConstraint("CK_CommercialCatalogItems_Values", "\"Width\" > 0 AND \"Height\" > 0 AND \"SquareMeters\" > 0 AND \"Quantity\" > 0 AND length(btrim(\"ItemNumber\")) > 0");
                    table.ForeignKey(
                        name: "FK_CommercialCatalogItems_CommercialCatalogs_CommercialCatalog~",
                        column: x => x.CommercialCatalogId,
                        principalTable: "CommercialCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialCatalogItemExtractions",
                columns: table => new
                {
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageStatus = table.Column<int>(type: "integer", nullable: false),
                    ImageStorageKey = table.Column<string>(type: "text", nullable: true),
                    ImageContentType = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_CommercialCatalogItemExtractions", x => x.CatalogItemId);
                    table.CheckConstraint("CK_CommercialCatalogItemExtractions_Image", "(\"ImageStatus\" = 1 AND \"ImageStorageKey\" IS NOT NULL AND \"ImageContentType\" IS NOT NULL) OR (\"ImageStatus\" IN (2, 3) AND \"ImageStorageKey\" IS NULL AND \"ImageContentType\" IS NULL)");
                    table.CheckConstraint("CK_CommercialCatalogItemExtractions_Values", "\"Width\" > 0 AND \"Height\" > 0 AND \"SquareMeters\" > 0 AND \"Quantity\" > 0 AND length(btrim(\"ItemNumber\")) > 0");
                    table.ForeignKey(
                        name: "FK_CommercialCatalogItemExtractions_CommercialCatalogItems_Cat~",
                        column: x => x.CatalogItemId,
                        principalTable: "CommercialCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetQuotationVersions_OriginalFileStorageKey",
                table: "BudgetQuotationVersions",
                column: "OriginalFileStorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BudgetQuotationVersions_ProjectId_VersionNumber",
                table: "BudgetQuotationVersions",
                columns: new[] { "ProjectId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialCatalogItemExtractions_ImageStorageKey",
                table: "CommercialCatalogItemExtractions",
                column: "ImageStorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialCatalogItems_CommercialCatalogId",
                table: "CommercialCatalogItems",
                column: "CommercialCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialCatalogItems_CommercialCatalogId_SourceRow",
                table: "CommercialCatalogItems",
                columns: new[] { "CommercialCatalogId", "SourceRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialCatalogs_BudgetQuotationVersionId",
                table: "CommercialCatalogs",
                column: "BudgetQuotationVersionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialCatalogItemExtractions");

            migrationBuilder.DropTable(
                name: "CommercialCatalogItems");

            migrationBuilder.DropTable(
                name: "CommercialCatalogs");

            migrationBuilder.DropTable(
                name: "BudgetQuotationVersions");
        }
    }
}
