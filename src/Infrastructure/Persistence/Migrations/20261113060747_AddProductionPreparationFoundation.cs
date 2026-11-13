using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPreparationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionPreparations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManifestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionPreparations", x => x.Id);
                    table.UniqueConstraint("AK_ProductionPreparations_Id_ReleaseVersionId", x => new { x.Id, x.ReleaseVersionId });
                    table.CheckConstraint("CK_ProductionPreparation_Revision", "\"Revision\" >= 0");
                    table.CheckConstraint("CK_ProductionPreparation_State", "\"State\" = 1");
                    table.ForeignKey(
                        name: "FK_ProductionPreparations_OpeningFilingReleases_ReleaseVersion~",
                        column: x => x.ReleaseVersionId,
                        principalTable: "OpeningFilingReleases",
                        principalColumn: "VersionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPreparations_OpeningFilingWorkspaces_FilingId_Pro~",
                        columns: x => new { x.FilingId, x.ProjectId },
                        principalTable: "OpeningFilingWorkspaces",
                        principalColumns: new[] { "FilingId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PreparationSourceDocuments",
                columns: table => new
                {
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparationSourceDocuments", x => new { x.PreparationId, x.DocumentId });
                    table.UniqueConstraint("AK_PreparationSourceDocuments_PreparationId_DocumentId_Sha256", x => new { x.PreparationId, x.DocumentId, x.Sha256 });
                    table.ForeignKey(
                        name: "FK_PreparationSourceDocuments_OpeningFilingDocuments_ReleaseVe~",
                        columns: x => new { x.ReleaseVersionId, x.DocumentId },
                        principalTable: "OpeningFilingDocuments",
                        principalColumns: new[] { "VersionId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PreparationSourceDocuments_ProductionPreparations_Preparati~",
                        columns: x => new { x.PreparationId, x.ReleaseVersionId },
                        principalTable: "ProductionPreparations",
                        principalColumns: new[] { "Id", "ReleaseVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PreparationSourceMembers",
                columns: table => new
                {
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparationSourceMembers", x => new { x.PreparationId, x.StructureId });
                    table.UniqueConstraint("AK_PreparationSourceMembers_PreparationId_StructureId_Op", x => new { x.PreparationId, x.StructureId, x.Op });
                    table.ForeignKey(
                        name: "FK_PreparationSourceMembers_OpeningFilingMembers_ReleaseVersio~",
                        columns: x => new { x.ReleaseVersionId, x.StructureId },
                        principalTable: "OpeningFilingMembers",
                        principalColumns: new[] { "VersionId", "StructureId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PreparationSourceMembers_ProductionPreparations_Preparation~",
                        columns: x => new { x.PreparationId, x.ReleaseVersionId },
                        principalTable: "ProductionPreparations",
                        principalColumns: new[] { "Id", "ReleaseVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionComponentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionComponentRevisions", x => x.Id);
                    table.UniqueConstraint("AK_ProductionComponentRevisions_Id_PreparationId", x => new { x.Id, x.PreparationId });
                    table.CheckConstraint("CK_ComponentRevision_Number", "\"Number\" > 0 AND \"Revision\" >= 0");
                    table.CheckConstraint("CK_ComponentRevision_State", "\"State\" = 1");
                    table.ForeignKey(
                        name: "FK_ProductionComponentRevisions_ProductionPreparations_Prepara~",
                        column: x => x.PreparationId,
                        principalTable: "ProductionPreparations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionCandidateRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Material = table.Column<int>(type: "integer", nullable: true),
                    DeclaredMaterial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProposedRoute = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OriginalDimensions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Measure = table.Column<decimal>(type: "numeric", nullable: true),
                    CutDimension = table.Column<decimal>(type: "numeric", nullable: true),
                    Width = table.Column<decimal>(type: "numeric", nullable: true),
                    Height = table.Column<decimal>(type: "numeric", nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    LeftAngle = table.Column<decimal>(type: "numeric", nullable: true),
                    RightAngle = table.Column<decimal>(type: "numeric", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    OriginalPieceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SpecificationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: true),
                    SourceRow = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sheet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CoordinatesJson = table.Column<string>(type: "jsonb", nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionCandidateRows", x => x.Id);
                    table.CheckConstraint("CK_Candidate_Page", "\"Page\" IS NULL OR \"Page\" > 0");
                    table.CheckConstraint("CK_Candidate_Quantity", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                    table.CheckConstraint("CK_Candidate_Route", "((\"Material\" IS NULL AND \"ProposedRoute\" IS NULL) OR (\"Material\" = 1 AND \"ProposedRoute\" = 1) OR (\"Material\" IN (2,3) AND \"ProposedRoute\" = 2)) IS TRUE");
                    table.ForeignKey(
                        name: "FK_ProductionCandidateRows_PreparationSourceDocuments_Preparat~",
                        columns: x => new { x.PreparationId, x.DocumentId, x.DocumentSha256 },
                        principalTable: "PreparationSourceDocuments",
                        principalColumns: new[] { "PreparationId", "DocumentId", "Sha256" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionCandidateRows_PreparationSourceMembers_Preparatio~",
                        columns: x => new { x.PreparationId, x.StructureId, x.Op },
                        principalTable: "PreparationSourceMembers",
                        principalColumns: new[] { "PreparationId", "StructureId", "Op" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionCandidateRows_ProductionComponentRevisions_Compon~",
                        columns: x => new { x.ComponentRevisionId, x.PreparationId },
                        principalTable: "ProductionComponentRevisions",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreparationSourceDocuments_PreparationId_ReleaseVersionId",
                table: "PreparationSourceDocuments",
                columns: new[] { "PreparationId", "ReleaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PreparationSourceDocuments_ReleaseVersionId_DocumentId",
                table: "PreparationSourceDocuments",
                columns: new[] { "ReleaseVersionId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PreparationSourceMembers_PreparationId_Op",
                table: "PreparationSourceMembers",
                columns: new[] { "PreparationId", "Op" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreparationSourceMembers_PreparationId_ReleaseVersionId",
                table: "PreparationSourceMembers",
                columns: new[] { "PreparationId", "ReleaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PreparationSourceMembers_ReleaseVersionId_StructureId",
                table: "PreparationSourceMembers",
                columns: new[] { "ReleaseVersionId", "StructureId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCandidateRows_ComponentRevisionId_PreparationId",
                table: "ProductionCandidateRows",
                columns: new[] { "ComponentRevisionId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCandidateRows_PreparationId_DocumentId_DocumentSh~",
                table: "ProductionCandidateRows",
                columns: new[] { "PreparationId", "DocumentId", "DocumentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionCandidateRows_PreparationId_StructureId_Op",
                table: "ProductionCandidateRows",
                columns: new[] { "PreparationId", "StructureId", "Op" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionComponentRevisions_PreparationId_Number",
                table: "ProductionComponentRevisions",
                columns: new[] { "PreparationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPreparations_FilingId_ProjectId",
                table: "ProductionPreparations",
                columns: new[] { "FilingId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPreparations_ReleaseVersionId",
                table: "ProductionPreparations",
                column: "ReleaseVersionId",
                unique: true);
            migrationBuilder.Sql("""

CREATE FUNCTION sng_preparation_source() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE manifest jsonb;
BEGIN
 IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Preparation source immutable' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS (
 SELECT 1 FROM "OpeningFilingReleases" r JOIN "OpeningFilingVersions" v ON v."Id"=r."VersionId"
 JOIN "OpeningFilingDeliveries" f ON f."Id"=r."FilingId" JOIN "OpeningAcceptances" a ON a."Id"=f."AcceptanceId"
 WHERE r."VersionId"=NEW."ReleaseVersionId" AND r."ProjectId"=NEW."ProjectId" AND r."FilingId"=NEW."FilingId"
 AND r."Sha256"=NEW."ManifestSha256" AND v."ConfirmedAt" IS NOT NULL AND v."ManifestCanonical"=r."Canonical"
 AND v."ManifestSha256"=r."Sha256" AND NOT a."IsLegacy"
 AND upper(encode(sha256(convert_to(r."Canonical",'UTF8')),'hex'))=r."Sha256")
 THEN RAISE EXCEPTION 'Invalid preparation release' USING ERRCODE='23514'; END IF;
 SELECT "Canonical"::jsonb INTO manifest FROM "OpeningFilingReleases" WHERE "VersionId"=NEW."ReleaseVersionId";
 IF (manifest->>'ProjectId')::uuid IS DISTINCT FROM NEW."ProjectId" OR (manifest->>'FilingId')::uuid IS DISTINCT FROM NEW."FilingId"
 OR (manifest->>'VersionId')::uuid IS DISTINCT FROM NEW."ReleaseVersionId" OR coalesce(jsonb_array_length(manifest->'Structures'),0)=0
 OR coalesce(jsonb_array_length(manifest->'Evidence'),0) <> jsonb_array_length(manifest->'Structures')
 OR EXISTS(SELECT 1 FROM jsonb_array_elements(manifest->'Structures') m WHERE NOT coalesce((m->>'Confirmed')::boolean,false)
 OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(manifest->'Evidence') e WHERE e->>'StructureId'=m->>'StructureId' AND coalesce((e->>'Attested')::boolean,false)))
 THEN RAISE EXCEPTION 'Incomplete preparation evidence' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER preparation_source BEFORE INSERT OR UPDATE OR DELETE ON "ProductionPreparations" FOR EACH ROW EXECUTE FUNCTION sng_preparation_source();
CREATE FUNCTION sng_preparation_complete() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE manifest jsonb;
BEGIN
 SELECT "Canonical"::jsonb INTO manifest FROM "OpeningFilingReleases" WHERE "VersionId"=NEW."ReleaseVersionId";
 IF (SELECT count(*) FROM "PreparationSourceMembers" WHERE "PreparationId"=NEW."Id") <> jsonb_array_length(manifest->'Structures')
 OR (SELECT count(*) FROM "PreparationSourceDocuments" WHERE "PreparationId"=NEW."Id") <> jsonb_array_length(manifest->'Documents')
 THEN RAISE EXCEPTION 'Incomplete preparation scope' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE CONSTRAINT TRIGGER preparation_complete AFTER INSERT ON "ProductionPreparations" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_preparation_complete();
CREATE FUNCTION sng_preparation_member() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Preparation scope immutable' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS (SELECT 1 FROM "OpeningFilingMembers" m JOIN "OpeningFilingReleases" r ON r."VersionId"=m."VersionId"
 WHERE m."VersionId"=NEW."ReleaseVersionId" AND m."StructureId"=NEW."StructureId" AND m."OpNormalized"=NEW."Op" AND m."Confirmed"
 AND EXISTS(SELECT 1 FROM jsonb_array_elements(r."Canonical"::jsonb->'Structures') j WHERE (j->>'StructureId')::uuid=m."StructureId" AND j->>'OpNormalized'=m."OpNormalized"))
 THEN RAISE EXCEPTION 'Invalid preparation OP' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER preparation_member BEFORE INSERT OR UPDATE OR DELETE ON "PreparationSourceMembers" FOR EACH ROW EXECUTE FUNCTION sng_preparation_member();
CREATE FUNCTION sng_preparation_document() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Preparation scope immutable' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS (SELECT 1 FROM "OpeningFilingDocuments" d JOIN "OpeningFilingReleases" r ON r."VersionId"=d."VersionId"
 WHERE d."VersionId"=NEW."ReleaseVersionId" AND d."DocumentId"=NEW."DocumentId" AND d."Sha256"=NEW."Sha256" AND d."Category"=NEW."Category" AND d."State"='Current'
 AND EXISTS(SELECT 1 FROM jsonb_array_elements(r."Canonical"::jsonb->'Documents') j WHERE (j->>'Id')::uuid=d."DocumentId" AND j->>'Sha256'=d."Sha256" AND j->>'Category'=d."Category"))
 THEN RAISE EXCEPTION 'Invalid preparation document' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER preparation_document BEFORE INSERT OR UPDATE OR DELETE ON "PreparationSourceDocuments" FOR EACH ROW EXECUTE FUNCTION sng_preparation_document();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION sng_preparation_complete() CASCADE; DROP FUNCTION sng_preparation_document() CASCADE; DROP FUNCTION sng_preparation_member() CASCADE; DROP FUNCTION sng_preparation_source() CASCADE;");
            migrationBuilder.DropTable(
                name: "ProductionCandidateRows");

            migrationBuilder.DropTable(
                name: "PreparationSourceDocuments");

            migrationBuilder.DropTable(
                name: "PreparationSourceMembers");

            migrationBuilder.DropTable(
                name: "ProductionComponentRevisions");

            migrationBuilder.DropTable(
                name: "ProductionPreparations");
        }
    }
}
