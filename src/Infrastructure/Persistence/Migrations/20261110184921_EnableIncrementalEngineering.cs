using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableIncrementalEngineering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "EngineeringWorkspaces",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "EngineeringAssignedStructures",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringAssignedStructures", x => new { x.ProjectId, x.StructureId });
                    table.ForeignKey(
                        name: "FK_EngineeringAssignedStructures_EngineeringEvidenceChecks_Pro~",
                        columns: x => new { x.ProjectId, x.SubmissionId, x.StructureId, x.EvidenceVersion },
                        principalTable: "EngineeringEvidenceChecks",
                        principalColumns: new[] { "ProjectId", "SubmissionId", "StructureId", "EvidenceVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringAssignedStructures_WorkPackageAssignments_Assign~",
                        columns: x => new { x.AssignmentId, x.ProjectId },
                        principalTable: "WorkPackageAssignments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringAssignedStructures_WorkPackageMembers_Assignment~",
                        columns: x => new { x.AssignmentId, x.StructureId },
                        principalTable: "WorkPackageMembers",
                        principalColumns: new[] { "AssignmentId", "StructureId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringOpeningStates",
                columns: table => new
                {
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibleSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CurrentDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfirmationCanonical = table.Column<string>(type: "text", nullable: true),
                    ConfirmationSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringOpeningStates", x => x.OpeningRecordId);
                    table.UniqueConstraint("AK_EngineeringOpeningStates_OpeningRecordId_ProjectId", x => new { x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_EngineeringOpening_Confirmed", "\"Revision\">=0 AND ((\"ConfirmedAt\" IS NULL AND \"ConfirmedBySubject\" IS NULL AND \"ConfirmationCanonical\" IS NULL AND \"ConfirmationSha256\" IS NULL) OR (\"ConfirmedAt\" IS NOT NULL AND \"ConfirmedBySubject\" IS NOT NULL AND \"CurrentDocumentId\" IS NOT NULL AND \"ConfirmationCanonical\" IS NOT NULL AND \"ConfirmationSha256\" ~ '^[A-F0-9]{64}$'))");
                    table.ForeignKey(
                        name: "FK_EngineeringOpeningStates_OpeningDocumentScopes_OpeningRecor~",
                        columns: x => new { x.OpeningRecordId, x.CurrentDocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringOpeningStates_OpeningRecords_OpeningRecordId_Pro~",
                        columns: x => new { x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRecords",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsIntake = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringPackages", x => x.Id);
                    table.UniqueConstraint("AK_EngineeringPackages_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_EngineeringPackage_State", "\"Revision\">=0 AND \"Status\" IN ('Open','Transferred','Completed') AND ((\"Status\"='Open' AND \"CompletedAt\" IS NULL) OR (\"Status\"<>'Open' AND \"CompletedAt\">=\"CreatedAt\"))");
                    table.ForeignKey(
                        name: "FK_EngineeringPackages_EngineeringWorkspaces_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "EngineeringWorkspaces",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringPackages_ProjectTasks_TaskId_ProjectId",
                        columns: x => new { x.TaskId, x.ProjectId },
                        principalTable: "ProjectTasks",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringPackages_WorkPackageAssignments_CurrentAssignmen~",
                        columns: x => new { x.CurrentAssignmentId, x.ProjectId },
                        principalTable: "WorkPackageAssignments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringWorkspaceEvents",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canonical = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringWorkspaceEvents", x => new { x.ProjectId, x.RequestKey });
                    table.CheckConstraint("CK_EngineeringWorkspaceEvent", "\"Revision\">=0 AND length(btrim(\"ActorSubject\"))>0 AND \"Fingerprint\" ~ '^[A-F0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_EngineeringWorkspaceEvents_EngineeringWorkspaces_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "EngineeringWorkspaces",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectTasks_EngineeringPackage",
                table: "ProjectTasks",
                sql: "\"Type\" <> 11 OR (\"Area\"=5 AND \"QuotationRound\" IS NULL AND \"DeadlineAt\" IS NULL AND \"WasOverdue\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringAssignedStructures_AssignmentId_ProjectId",
                table: "EngineeringAssignedStructures",
                columns: new[] { "AssignmentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringAssignedStructures_AssignmentId_StructureId",
                table: "EngineeringAssignedStructures",
                columns: new[] { "AssignmentId", "StructureId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringAssignedStructures_ProjectId_SubmissionId_Struct~",
                table: "EngineeringAssignedStructures",
                columns: new[] { "ProjectId", "SubmissionId", "StructureId", "EvidenceVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringOpeningStates_OpeningRecordId_CurrentDocumentId_~",
                table: "EngineeringOpeningStates",
                columns: new[] { "OpeningRecordId", "CurrentDocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringPackages_CurrentAssignmentId_ProjectId",
                table: "EngineeringPackages",
                columns: new[] { "CurrentAssignmentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringPackages_ProjectId",
                table: "EngineeringPackages",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringPackages_TaskId",
                table: "EngineeringPackages",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringPackages_TaskId_ProjectId",
                table: "EngineeringPackages",
                columns: new[] { "TaskId", "ProjectId" });
            migrationBuilder.Sql("""
                INSERT INTO "EngineeringPackages" ("Id","ProjectId","CurrentAssignmentId","TaskId","IsIntake","CreatedAt","CreatedBySubject","Revision","Status","CompletedAt")
                SELECT a."PackageId",a."ProjectId",a."Id",NULL,true,a."AssignedAt",a."Subject",0,'Open',NULL
                FROM "WorkPackageAssignments" a JOIN "MeasurementChecklists" c ON c."ProjectId"=a."ProjectId"
                WHERE c."IsIncremental" AND a."Area"=5 AND a."EndedAt" IS NULL;
                INSERT INTO "EngineeringAssignedStructures" ("ProjectId","StructureId","SubmissionId","EvidenceVersion","AssignmentId")
                SELECT m."ProjectId",m."StructureId",m."SubmissionId",m."EvidenceVersion",m."AssignmentId"
                FROM "WorkPackageMembers" m JOIN "EngineeringPackages" p ON p."CurrentAssignmentId"=m."AssignmentId";
                CREATE FUNCTION sng_engineering_confirmed_guard() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE acta uuid; project uuid; structure uuid;
                BEGIN
                  IF TG_TABLE_NAME='EngineeringOpeningStates' THEN
                    IF TG_OP='DELETE' OR OLD."ConfirmedAt" IS NOT NULL THEN RAISE EXCEPTION 'Confirmed engineering evidence is immutable' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                  ELSIF TG_TABLE_NAME IN ('OpeningRecordMembers','OpeningDocumentScopes','OpeningDocumentMembers') THEN
                    acta:=NEW."OpeningRecordId";
                    IF EXISTS(SELECT 1 FROM "EngineeringOpeningStates" WHERE "OpeningRecordId"=acta AND "ConfirmedAt" IS NOT NULL) THEN
                      RAISE EXCEPTION 'Confirmed manifest and documents are immutable' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  ELSIF TG_TABLE_NAME='EngineeringAssignedStructures' THEN
                    IF TG_OP='DELETE' OR (NEW."ProjectId",NEW."StructureId",NEW."SubmissionId",NEW."EvidenceVersion") IS DISTINCT FROM (OLD."ProjectId",OLD."StructureId",OLD."SubmissionId",OLD."EvidenceVersion") THEN
                      RAISE EXCEPTION 'Engineering evidence identity is immutable' USING ERRCODE='23514';
                    END IF;
                    project:=OLD."ProjectId";structure:=OLD."StructureId";
                    IF EXISTS(SELECT 1 FROM "ActiveOpeningRecordMembers" WHERE "ProjectId"=project AND "StructureId"=structure) THEN
                      RAISE EXCEPTION 'Acta member assignment is reserved' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  ELSE
                    project:=OLD."ProjectId";structure:=OLD."StructureId";
                    IF EXISTS(SELECT 1 FROM "ActiveOpeningRecordMembers" m JOIN "EngineeringOpeningStates" s ON s."OpeningRecordId"=m."OpeningRecordId"
                              WHERE m."ProjectId"=project AND m."StructureId"=structure AND s."ConfirmedAt" IS NOT NULL) THEN
                      RAISE EXCEPTION 'Confirmed checks are immutable' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                  END IF;
                END $body$;
                CREATE TRIGGER engineering_state_frozen BEFORE UPDATE OR DELETE ON "EngineeringOpeningStates" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_manifest_frozen BEFORE INSERT ON "OpeningRecordMembers" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_document_frozen BEFORE INSERT ON "OpeningDocumentScopes" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_scope_frozen BEFORE INSERT ON "OpeningDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_claim_frozen BEFORE UPDATE OR DELETE ON "EngineeringAssignedStructures" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_check_frozen BEFORE UPDATE OR DELETE ON "EngineeringEvidenceChecks" FOR EACH ROW EXECUTE FUNCTION sng_engineering_confirmed_guard();
                CREATE TRIGGER engineering_audit_frozen BEFORE UPDATE OR DELETE ON "EngineeringWorkspaceEvents" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "EngineeringWorkspaceEvents") OR EXISTS(SELECT 1 FROM "EngineeringOpeningStates") OR EXISTS(SELECT 1 FROM "EngineeringPackages" WHERE NOT "IsIntake") THEN
                    RAISE EXCEPTION 'Cannot downgrade native Engineering evidence' USING ERRCODE='23514';
                  END IF;
                END $$;
                DROP TRIGGER engineering_state_frozen ON "EngineeringOpeningStates";
                DROP TRIGGER engineering_manifest_frozen ON "OpeningRecordMembers";
                DROP TRIGGER engineering_document_frozen ON "OpeningDocumentScopes";
                DROP TRIGGER engineering_scope_frozen ON "OpeningDocumentMembers";
                DROP TRIGGER engineering_claim_frozen ON "EngineeringAssignedStructures";
                DROP TRIGGER engineering_check_frozen ON "EngineeringEvidenceChecks";
                DROP TRIGGER engineering_audit_frozen ON "EngineeringWorkspaceEvents";
                DROP FUNCTION sng_engineering_confirmed_guard();
                """);
            migrationBuilder.DropTable(
                name: "EngineeringAssignedStructures");

            migrationBuilder.DropTable(
                name: "EngineeringOpeningStates");

            migrationBuilder.DropTable(
                name: "EngineeringPackages");

            migrationBuilder.DropTable(
                name: "EngineeringWorkspaceEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectTasks_EngineeringPackage",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "EngineeringWorkspaces");
        }
    }
}
