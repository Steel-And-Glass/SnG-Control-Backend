using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrateDocumentExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionPreparation_State",
                table: "ProductionPreparations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ComponentRevision_State",
                table: "ProductionComponentRevisions");

            migrationBuilder.CreateTable(
                name: "DocumentExtractionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: false),
                    EventsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExtractionJobs", x => x.Id);
                    table.CheckConstraint("CK_ExtractionJob_Lease", "(\"State\"=2 AND \"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"State\"<>2 AND \"LeaseToken\" IS NULL AND \"LeaseUntil\" IS NULL)");
                    table.CheckConstraint("CK_ExtractionJob_State", "\"State\" IN (1,2,3,4,5) AND \"Attempts\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_DocumentExtractionJobs_ProductionPreparations_PreparationId",
                        column: x => x.PreparationId,
                        principalTable: "ProductionPreparations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionPreparation_State",
                table: "ProductionPreparations",
                sql: "\"State\" IN (1,2,3,4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ComponentRevision_State",
                table: "ProductionComponentRevisions",
                sql: "\"State\" IN (1,2,3,4)");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractionJobs_PreparationId_ExtractorVersion",
                table: "DocumentExtractionJobs",
                columns: new[] { "PreparationId", "ExtractorVersion" },
                unique: true);
            migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION sng_preparation_source() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE manifest jsonb;
BEGIN
 IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Preparation source immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP = 'UPDATE' AND ((to_jsonb(NEW)-ARRAY['State','Revision','ExtractorVersion']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['State','Revision','ExtractorVersion'])
 OR NEW."Revision" <> OLD."Revision"+1 OR NEW."ExtractorVersion" IS NULL
 OR NOT ((OLD."State"=1 AND NEW."State" IN(2,3,4)) OR (OLD."State"=3 AND NEW."State"=1)))
 THEN RAISE EXCEPTION 'Invalid extraction transition or source change' USING ERRCODE='23514'; END IF;
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
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $guard$ BEGIN
 IF EXISTS(SELECT 1 FROM "DocumentExtractionJobs") THEN
 RAISE EXCEPTION 'Cannot remove extraction audit; explicit retention procedure required' USING ERRCODE='23514';
 END IF;
END $guard$;
""");
            migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION sng_preparation_source() RETURNS trigger LANGUAGE plpgsql AS $f$
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
""");
            migrationBuilder.DropTable(
                name: "DocumentExtractionJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionPreparation_State",
                table: "ProductionPreparations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ComponentRevision_State",
                table: "ProductionComponentRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionPreparation_State",
                table: "ProductionPreparations",
                sql: "\"State\" = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ComponentRevision_State",
                table: "ProductionComponentRevisions",
                sql: "\"State\" = 1");
        }
    }
}
