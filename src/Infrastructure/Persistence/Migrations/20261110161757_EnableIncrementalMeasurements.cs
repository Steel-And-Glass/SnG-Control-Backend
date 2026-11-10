using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableIncrementalMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIncremental",
                table: "MeasurementChecklists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EngineeringCheckEvents",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringCheckEvents", x => new { x.ProjectId, x.RequestKey });
                    table.ForeignKey(
                        name: "FK_EngineeringCheckEvents_EngineeringEvidenceChecks_ProjectId_~",
                        columns: x => new { x.ProjectId, x.SubmissionId, x.StructureId, x.EvidenceVersion },
                        principalTable: "EngineeringEvidenceChecks",
                        principalColumns: new[] { "ProjectId", "SubmissionId", "StructureId", "EvidenceVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringCheckEvents_ProjectId_StructureId_EvidenceVersio~",
                table: "EngineeringCheckEvents",
                columns: new[] { "ProjectId", "StructureId", "EvidenceVersion", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringCheckEvents_ProjectId_SubmissionId_StructureId_E~",
                table: "EngineeringCheckEvents",
                columns: new[] { "ProjectId", "SubmissionId", "StructureId", "EvidenceVersion" });
            migrationBuilder.Sql("""

CREATE FUNCTION sng_incremental_structure_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE physical_id uuid;
BEGIN
 IF TG_TABLE_NAME='MeasurementStructures' THEN physical_id := OLD."Id"; ELSE physical_id := NEW."StructureId"; END IF;
 IF EXISTS (SELECT 1 FROM "MeasurementSubmissionMembers" m JOIN "MeasurementSubmissions" s ON s."Id"=m."SubmissionId" WHERE m."StructureId"=physical_id AND NOT s."IsLegacy") THEN
  RAISE EXCEPTION 'Submitted physical evidence is immutable' USING ERRCODE='23514', CONSTRAINT='CK_IncrementalStructureFrozen';
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END $fn$;
CREATE TRIGGER incremental_structure_frozen BEFORE UPDATE OR DELETE ON "MeasurementStructures" FOR EACH ROW EXECUTE FUNCTION sng_incremental_structure_guard();
CREATE TRIGGER incremental_sketch_frozen BEFORE INSERT ON "MeasurementSketchVersions" FOR EACH ROW EXECUTE FUNCTION sng_incremental_structure_guard();
CREATE TRIGGER incremental_audit_frozen BEFORE UPDATE OR DELETE ON "EngineeringCheckEvents" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE FUNCTION sng_incremental_mode_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
 IF TG_TABLE_NAME='MeasurementChecklists' THEN
  IF NEW."IsIncremental" IS DISTINCT FROM OLD."IsIncremental" OR (NEW."IsIncremental" AND NEW."SubmissionId" IS NOT NULL) THEN
   RAISE EXCEPTION 'Workspace mode and incremental lifecycle are immutable' USING ERRCODE='23514';
  END IF;
 ELSE
  IF EXISTS (SELECT 1 FROM "MeasurementChecklists" WHERE "ProjectId"=NEW."ProjectId" AND "IsIncremental") THEN
   RAISE EXCEPTION 'Incremental workspace cannot enter historical global Engineering' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $fn$;
CREATE TRIGGER incremental_mode_frozen BEFORE UPDATE ON "MeasurementChecklists" FOR EACH ROW EXECUTE FUNCTION sng_incremental_mode_guard();
CREATE TRIGGER incremental_no_legacy_engineering BEFORE INSERT ON "EngineeringChecklists" FOR EACH ROW EXECUTE FUNCTION sng_incremental_mode_guard();
CREATE FUNCTION sng_incremental_delivery_complete() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
 IF NOT NEW."IsLegacy" AND EXISTS (SELECT 1 FROM "MeasurementChecklists" WHERE "Id"=NEW."ChecklistId" AND "IsIncremental") THEN
  IF NOT EXISTS(SELECT 1 FROM "SubmissionReports" WHERE "SubmissionId"=NEW."Id" AND "Version"=1 AND "Pdf" IS NOT NULL)
   OR NOT EXISTS(SELECT 1 FROM "EngineeringReceipts" WHERE "SubmissionId"=NEW."Id" AND "ProjectId"=NEW."ProjectId")
   OR EXISTS(SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=NEW."Id" AND m."EvidenceVersion"=1 AND NOT EXISTS(SELECT 1 FROM "EngineeringEvidenceChecks" c WHERE c."SubmissionId"=m."SubmissionId" AND c."StructureId"=m."StructureId" AND c."EvidenceVersion"=1)) THEN
   RAISE EXCEPTION 'Delivery requires report and complete Engineering reception' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $fn$;
CREATE CONSTRAINT TRIGGER incremental_delivery_complete AFTER INSERT ON "MeasurementSubmissions" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_incremental_delivery_complete();

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $fn$ BEGIN IF EXISTS(SELECT 1 FROM "MeasurementChecklists" WHERE "IsIncremental") OR EXISTS(SELECT 1 FROM "EngineeringCheckEvents") THEN RAISE EXCEPTION 'Cannot remove an active incremental workflow'; END IF; END $fn$;
DROP TRIGGER incremental_delivery_complete ON "MeasurementSubmissions";
DROP FUNCTION sng_incremental_delivery_complete();
DROP TRIGGER incremental_no_legacy_engineering ON "EngineeringChecklists";
DROP TRIGGER incremental_mode_frozen ON "MeasurementChecklists";
DROP FUNCTION sng_incremental_mode_guard();
DROP TRIGGER incremental_structure_frozen ON "MeasurementStructures";
DROP TRIGGER incremental_sketch_frozen ON "MeasurementSketchVersions";
DROP FUNCTION sng_incremental_structure_guard();

""");
            migrationBuilder.DropTable(
                name: "EngineeringCheckEvents");

            migrationBuilder.DropColumn(
                name: "IsIncremental",
                table: "MeasurementChecklists");
        }
    }
}
