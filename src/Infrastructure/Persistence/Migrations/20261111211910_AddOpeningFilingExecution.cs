using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningFilingExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectDocuments_Id_ProjectId",
                table: "ProjectDocuments",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OpeningFilingDeliveries_Id_ProjectId",
                table: "OpeningFilingDeliveries",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "OpeningFilingWorkspaces",
                columns: table => new
                {
                    FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibleSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    NumberOriginal = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    NumberNormalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FirstConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingWorkspaces", x => x.FilingId);
                    table.UniqueConstraint("AK_OpeningFilingWorkspaces_FilingId_ProjectId", x => new { x.FilingId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_OpeningFilingWorkspaces_OpeningFilingDeliveries_FilingId_Pr~",
                        columns: x => new { x.FilingId, x.ProjectId },
                        principalTable: "OpeningFilingDeliveries",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingEvents",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canonical = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingEvents", x => new { x.ProjectId, x.RequestKey });
                    table.ForeignKey(
                        name: "FK_OpeningFilingEvents_OpeningFilingWorkspaces_FilingId_Projec~",
                        columns: x => new { x.FilingId, x.ProjectId },
                        principalTable: "OpeningFilingWorkspaces",
                        principalColumns: new[] { "FilingId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ManifestCanonical = table.Column<string>(type: "text", nullable: true),
                    ManifestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingVersions", x => x.Id);
                    table.UniqueConstraint("AK_OpeningFilingVersions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_OpeningFilingVersions_OpeningFilingWorkspaces_FilingId_Proj~",
                        columns: x => new { x.FilingId, x.ProjectId },
                        principalTable: "OpeningFilingWorkspaces",
                        principalColumns: new[] { "FilingId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingDocuments",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileVersion = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingDocuments", x => new { x.VersionId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_OpeningFilingDocuments_OpeningFilingVersions_VersionId_Proj~",
                        columns: x => new { x.VersionId, x.ProjectId },
                        principalTable: "OpeningFilingVersions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningFilingDocuments_ProjectDocuments_DocumentId_ProjectId",
                        columns: x => new { x.DocumentId, x.ProjectId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingMembers",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpOriginal = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OpNormalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingMembers", x => new { x.VersionId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_OpeningFilingMembers_MeasurementStructures_StructureId_Proj~",
                        columns: x => new { x.StructureId, x.ProjectId },
                        principalTable: "MeasurementStructures",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningFilingMembers_OpeningFilingVersions_VersionId_Projec~",
                        columns: x => new { x.VersionId, x.ProjectId },
                        principalTable: "OpeningFilingVersions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningFilingReleases",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Canonical = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningFilingReleases", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_OpeningFilingReleases_OpeningFilingVersions_VersionId_Proje~",
                        columns: x => new { x.VersionId, x.ProjectId },
                        principalTable: "OpeningFilingVersions",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningFilingReleases_OpeningFilingWorkspaces_FilingId_Proj~",
                        columns: x => new { x.FilingId, x.ProjectId },
                        principalTable: "OpeningFilingWorkspaces",
                        principalColumns: new[] { "FilingId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDocuments_DocumentId_ProjectId",
                table: "OpeningFilingDocuments",
                columns: new[] { "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDocuments_VersionId_LineageId",
                table: "OpeningFilingDocuments",
                columns: new[] { "VersionId", "LineageId" },
                unique: true,
                filter: "\"State\" = 'Current'");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingDocuments_VersionId_ProjectId",
                table: "OpeningFilingDocuments",
                columns: new[] { "VersionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingEvents_FilingId_ProjectId",
                table: "OpeningFilingEvents",
                columns: new[] { "FilingId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingMembers_StructureId_ProjectId",
                table: "OpeningFilingMembers",
                columns: new[] { "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingMembers_VersionId_OpNormalized",
                table: "OpeningFilingMembers",
                columns: new[] { "VersionId", "OpNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingMembers_VersionId_ProjectId",
                table: "OpeningFilingMembers",
                columns: new[] { "VersionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingReleases_FilingId_ProjectId",
                table: "OpeningFilingReleases",
                columns: new[] { "FilingId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingReleases_VersionId_ProjectId",
                table: "OpeningFilingReleases",
                columns: new[] { "VersionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingVersions_FilingId",
                table: "OpeningFilingVersions",
                column: "FilingId",
                unique: true,
                filter: "\"ConfirmedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingVersions_FilingId_Number",
                table: "OpeningFilingVersions",
                columns: new[] { "FilingId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingVersions_FilingId_ProjectId",
                table: "OpeningFilingVersions",
                columns: new[] { "FilingId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningFilingWorkspaces_ProjectId_NumberNormalized",
                table: "OpeningFilingWorkspaces",
                columns: new[] { "ProjectId", "NumberNormalized" },
                unique: true,
                filter: "\"NumberNormalized\" IS NOT NULL");
            migrationBuilder.Sql("""

CREATE FUNCTION sng_filing_draft_child() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE v uuid;
BEGIN
 v := CASE WHEN TG_OP='DELETE' THEN OLD."VersionId" ELSE NEW."VersionId" END;
 IF TG_OP='UPDATE' AND (OLD."VersionId"<>NEW."VersionId" OR OLD."ProjectId"<>NEW."ProjectId") THEN RAISE EXCEPTION 'Filing identity immutable' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM "OpeningFilingVersions" WHERE "Id"=v AND "ConfirmedAt" IS NOT NULL) THEN RAISE EXCEPTION 'Confirmed filing immutable' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='OpeningFilingMembers' AND TG_OP<>'DELETE' THEN
   IF NOT EXISTS(SELECT 1 FROM "OpeningFilingVersions" fv JOIN "OpeningFilingDeliveries" f ON f."Id"=fv."FilingId" JOIN "OpeningAcceptanceMembers" m ON m."AcceptanceId"=f."AcceptanceId" AND m."ProjectId"=fv."ProjectId" WHERE fv."Id"=v AND m."StructureId"=NEW."StructureId") THEN RAISE EXCEPTION 'Foreign filing member' USING ERRCODE='23514'; END IF;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END; $f$;
CREATE TRIGGER filing_draft_child BEFORE INSERT OR UPDATE OR DELETE ON "OpeningFilingMembers" FOR EACH ROW EXECUTE FUNCTION sng_filing_draft_child();
CREATE TRIGGER filing_draft_child BEFORE INSERT OR UPDATE OR DELETE ON "OpeningFilingDocuments" FOR EACH ROW EXECUTE FUNCTION sng_filing_draft_child();
CREATE FUNCTION sng_filing_version() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP='DELETE' OR OLD."ConfirmedAt" IS NOT NULL OR NEW."Id"<>OLD."Id" OR NEW."FilingId"<>OLD."FilingId" OR NEW."ProjectId"<>OLD."ProjectId" OR NEW."Number"<>OLD."Number" THEN RAISE EXCEPTION 'Filing version immutable' USING ERRCODE='23514'; END IF;
 IF NEW."ConfirmedAt" IS NOT NULL THEN
  IF NEW."ManifestCanonical" IS NULL OR NEW."ManifestSha256" !~ '^[A-F0-9]{64}$' OR NEW."ConfirmedBySubject" IS NULL THEN RAISE EXCEPTION 'Missing filing manifest' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM "OpeningFilingWorkspaces" w JOIN "OpeningFilingDeliveries" f ON f."Id"=w."FilingId" JOIN "OpeningAcceptances" a ON a."Id"=f."AcceptanceId" WHERE w."FilingId"=NEW."FilingId" AND w."NumberNormalized" IS NOT NULL AND NOT a."IsLegacy" AND w."ResponsibleSubject"=NEW."ConfirmedBySubject") THEN RAISE EXCEPTION 'Invalid filing origin' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM "OpeningFilingMembers" WHERE "VersionId"=NEW."Id") OR EXISTS(SELECT 1 FROM "OpeningFilingMembers" WHERE "VersionId"=NEW."Id" AND NOT "Confirmed") THEN RAISE EXCEPTION 'Unconfirmed OP scope' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM "OpeningFilingDeliveries" f JOIN "OpeningAcceptanceMembers" m ON m."AcceptanceId"=f."AcceptanceId" WHERE f."Id"=NEW."FilingId" AND NOT EXISTS(SELECT 1 FROM "OpeningFilingMembers" fm WHERE fm."VersionId"=NEW."Id" AND fm."StructureId"=m."StructureId")) THEN RAISE EXCEPTION 'Incomplete filing scope' USING ERRCODE='23514'; END IF;
  IF (SELECT count(DISTINCT "Category") FROM "OpeningFilingDocuments" WHERE "VersionId"=NEW."Id" AND "State"='Current' AND "Category"<>'Additional')<>7 THEN RAISE EXCEPTION 'Missing filing categories' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER filing_version BEFORE UPDATE OR DELETE ON "OpeningFilingVersions" FOR EACH ROW EXECUTE FUNCTION sng_filing_version();
CREATE FUNCTION sng_filing_workspace() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP='DELETE' OR NEW."FilingId"<>OLD."FilingId" OR NEW."ProjectId"<>OLD."ProjectId" OR NEW."ResponsibleSubject"<>OLD."ResponsibleSubject" OR (OLD."FirstConfirmedAt" IS NOT NULL AND (NEW."FirstConfirmedAt" IS DISTINCT FROM OLD."FirstConfirmedAt" OR NEW."NumberOriginal" IS DISTINCT FROM OLD."NumberOriginal" OR NEW."NumberNormalized" IS DISTINCT FROM OLD."NumberNormalized")) THEN RAISE EXCEPTION 'Filing identity immutable' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER filing_workspace BEFORE UPDATE OR DELETE ON "OpeningFilingWorkspaces" FOR EACH ROW EXECUTE FUNCTION sng_filing_workspace();
CREATE TRIGGER filing_event_immutable BEFORE UPDATE OR DELETE ON "OpeningFilingEvents" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER filing_release_immutable BEFORE UPDATE OR DELETE ON "OpeningFilingReleases" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE FUNCTION sng_filing_release() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM "OpeningFilingVersions" v JOIN "OpeningFilingDeliveries" f ON f."Id"=v."FilingId" JOIN "ProjectTasks" t ON t."Id"=f."TaskId" WHERE v."Id"=NEW."VersionId" AND v."FilingId"=NEW."FilingId" AND v."ProjectId"=NEW."ProjectId" AND v."ConfirmedAt"=NEW."At" AND v."ManifestCanonical"=NEW."Canonical" AND v."ManifestSha256"=NEW."Sha256" AND t."Status"=3) THEN RAISE EXCEPTION 'Invalid filing release' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE CONSTRAINT TRIGGER filing_release AFTER INSERT ON "OpeningFilingReleases" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_filing_release();
CREATE FUNCTION sng_filing_document_identity() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF EXISTS(SELECT 1 FROM "OpeningFilingDocuments" WHERE "DocumentId"=OLD."Id") THEN RAISE EXCEPTION 'Filing document immutable; replace through a new document' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END; $f$;
CREATE TRIGGER filing_document_identity BEFORE UPDATE OR DELETE ON "ProjectDocuments" FOR EACH ROW EXECUTE FUNCTION sng_filing_document_identity();
ALTER TABLE "OpeningFilingDocuments" ADD CONSTRAINT "CK_FilingDocument" CHECK ("Category" IN ('Structures','Sections','Plans','UnoptimizedCutList','Glass','Materials','GlassModulation','Additional') AND "State" IN ('Current','Replaced','Withdrawn') AND "FileVersion">0 AND "Sha256" ~ '^[A-F0-9]{64}$');
ALTER TABLE "OpeningFilingMembers" ADD CONSTRAINT "CK_FilingOp" CHECK (length("OpNormalized")>0 AND "OpNormalized"=upper(replace(btrim("OpOriginal"),' ','')));

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION sng_filing_draft_child() CASCADE; DROP FUNCTION sng_filing_version() CASCADE; DROP FUNCTION sng_filing_workspace() CASCADE; DROP FUNCTION sng_filing_release() CASCADE; DROP FUNCTION sng_filing_document_identity() CASCADE;");
            migrationBuilder.DropTable(
                name: "OpeningFilingDocuments");

            migrationBuilder.DropTable(
                name: "OpeningFilingEvents");

            migrationBuilder.DropTable(
                name: "OpeningFilingMembers");

            migrationBuilder.DropTable(
                name: "OpeningFilingReleases");

            migrationBuilder.DropTable(
                name: "OpeningFilingVersions");

            migrationBuilder.DropTable(
                name: "OpeningFilingWorkspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectDocuments_Id_ProjectId",
                table: "ProjectDocuments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OpeningFilingDeliveries_Id_ProjectId",
                table: "OpeningFilingDeliveries");
        }
    }
}
