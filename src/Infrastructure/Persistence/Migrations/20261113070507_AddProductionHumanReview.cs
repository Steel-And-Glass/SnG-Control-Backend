using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionHumanReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentExtractionJobs_PreparationId_ExtractorVersion",
                table: "DocumentExtractionJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "ComponentRevisionId",
                table: "DocumentExtractionJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "DocumentExtractionJobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ReextractReason",
                table: "DocumentExtractionJobs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductionCandidateRows_Id_PreparationId",
                table: "ProductionCandidateRows",
                columns: new[] { "Id", "PreparationId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentExtractionJobs_Id_PreparationId",
                table: "DocumentExtractionJobs",
                columns: new[] { "Id", "PreparationId" });

            migrationBuilder.CreateTable(
                name: "ProductionReviewAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionReviewAttachments", x => x.Id);
                    table.CheckConstraint("CK_ReviewAttachment_Size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 26214400");
                    table.ForeignKey(
                        name: "FK_ProductionReviewAttachments_ProductionPreparations_Preparat~",
                        column: x => x.PreparationId,
                        principalTable: "ProductionPreparations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DataRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionReviews", x => x.Id);
                    table.UniqueConstraint("AK_ProductionReviews_Id_PreparationId", x => new { x.Id, x.PreparationId });
                    table.CheckConstraint("CK_Review_State", "\"State\" IN (1,2,3) AND \"Revision\" >= 0 AND \"DataRevision\" >= 0");
                    table.ForeignKey(
                        name: "FK_ProductionReviews_DocumentExtractionJobs_ExtractionJobId_Pr~",
                        columns: x => new { x.ExtractionJobId, x.PreparationId },
                        principalTable: "DocumentExtractionJobs",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionReviews_ProductionComponentRevisions_SourceRevisi~",
                        columns: x => new { x.SourceRevisionId, x.PreparationId },
                        principalTable: "ProductionComponentRevisions",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionReviews_ProductionPreparations_PreparationId",
                        column: x => x.PreparationId,
                        principalTable: "ProductionPreparations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Canonical = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionApprovals_ProductionReviews_ReviewId_PreparationId",
                        columns: x => new { x.ReviewId, x.PreparationId },
                        principalTable: "ProductionReviews",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionReviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionReviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionReviewEvents_ProductionReviews_ReviewId_Preparati~",
                        columns: x => new { x.ReviewId, x.PreparationId },
                        principalTable: "ProductionReviews",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionReviewIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResolvedDataRevision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionReviewIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionReviewIssues_ProductionReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "ProductionReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionReviewRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalJson = table.Column<string>(type: "jsonb", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    Excluded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionReviewRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionReviewRows_PreparationSourceDocuments_Preparation~",
                        columns: x => new { x.PreparationId, x.DocumentId, x.DocumentSha256 },
                        principalTable: "PreparationSourceDocuments",
                        principalColumns: new[] { "PreparationId", "DocumentId", "Sha256" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionReviewRows_PreparationSourceMembers_PreparationId~",
                        columns: x => new { x.PreparationId, x.StructureId, x.Op },
                        principalTable: "PreparationSourceMembers",
                        principalColumns: new[] { "PreparationId", "StructureId", "Op" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionReviewRows_ProductionCandidateRows_CandidateId_Pr~",
                        columns: x => new { x.CandidateId, x.PreparationId },
                        principalTable: "ProductionCandidateRows",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionReviewRows_ProductionReviews_ReviewId_Preparation~",
                        columns: x => new { x.ReviewId, x.PreparationId },
                        principalTable: "ProductionReviews",
                        principalColumns: new[] { "Id", "PreparationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractionJobs_ComponentRevisionId_PreparationId",
                table: "DocumentExtractionJobs",
                columns: new[] { "ComponentRevisionId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractionJobs_PreparationId_ExtractorVersion_Gener~",
                table: "DocumentExtractionJobs",
                columns: new[] { "PreparationId", "ExtractorVersion", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_PreparationId",
                table: "ProductionApprovals",
                column: "PreparationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_ReviewId",
                table: "ProductionApprovals",
                column: "ReviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_ReviewId_PreparationId",
                table: "ProductionApprovals",
                columns: new[] { "ReviewId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewAttachments_PreparationId_RequestKey",
                table: "ProductionReviewAttachments",
                columns: new[] { "PreparationId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewEvents_PreparationId_RequestKey",
                table: "ProductionReviewEvents",
                columns: new[] { "PreparationId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewEvents_ReviewId_PreparationId",
                table: "ProductionReviewEvents",
                columns: new[] { "ReviewId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewIssues_ReviewId",
                table: "ProductionReviewIssues",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewRows_CandidateId_PreparationId",
                table: "ProductionReviewRows",
                columns: new[] { "CandidateId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewRows_PreparationId_DocumentId_DocumentSha256",
                table: "ProductionReviewRows",
                columns: new[] { "PreparationId", "DocumentId", "DocumentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewRows_PreparationId_StructureId_Op",
                table: "ProductionReviewRows",
                columns: new[] { "PreparationId", "StructureId", "Op" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewRows_ReviewId_CandidateId",
                table: "ProductionReviewRows",
                columns: new[] { "ReviewId", "CandidateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviewRows_ReviewId_PreparationId",
                table: "ProductionReviewRows",
                columns: new[] { "ReviewId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviews_ExtractionJobId",
                table: "ProductionReviews",
                column: "ExtractionJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviews_ExtractionJobId_PreparationId",
                table: "ProductionReviews",
                columns: new[] { "ExtractionJobId", "PreparationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviews_PreparationId_Number",
                table: "ProductionReviews",
                columns: new[] { "PreparationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionReviews_SourceRevisionId_PreparationId",
                table: "ProductionReviews",
                columns: new[] { "SourceRevisionId", "PreparationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentExtractionJobs_ProductionComponentRevisions_Compone~",
                table: "DocumentExtractionJobs",
                columns: new[] { "ComponentRevisionId", "PreparationId" },
                principalTable: "ProductionComponentRevisions",
                principalColumns: new[] { "Id", "PreparationId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""

CREATE FUNCTION sng_review_append_only() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN RAISE EXCEPTION 'Immutable review evidence' USING ERRCODE='23514'; END; $f$;
CREATE TRIGGER review_events_immutable BEFORE UPDATE OR DELETE ON "ProductionReviewEvents" FOR EACH ROW EXECUTE FUNCTION sng_review_append_only();
CREATE TRIGGER review_approvals_immutable BEFORE UPDATE OR DELETE ON "ProductionApprovals" FOR EACH ROW EXECUTE FUNCTION sng_review_append_only();
CREATE TRIGGER review_attachments_immutable BEFORE UPDATE OR DELETE ON "ProductionReviewAttachments" FOR EACH ROW EXECUTE FUNCTION sng_review_append_only();
CREATE TRIGGER extraction_rows_immutable BEFORE UPDATE OR DELETE ON "ProductionCandidateRows" FOR EACH ROW EXECUTE FUNCTION sng_review_append_only();
CREATE TRIGGER extraction_revisions_immutable BEFORE UPDATE OR DELETE ON "ProductionComponentRevisions" FOR EACH ROW EXECUTE FUNCTION sng_review_append_only();
CREATE FUNCTION sng_review_job_frozen() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF OLD."State" IN(3,4) THEN RAISE EXCEPTION 'Extraction result immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END; $f$;
CREATE TRIGGER extraction_job_frozen BEFORE UPDATE OR DELETE ON "DocumentExtractionJobs" FOR EACH ROW EXECUTE FUNCTION sng_review_job_frozen();
CREATE FUNCTION sng_review_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP='DELETE' OR OLD."State"<>1 THEN RAISE EXCEPTION 'Review frozen' USING ERRCODE='23514'; END IF;
 IF (to_jsonb(NEW)-ARRAY['Revision','DataRevision','State']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Revision','DataRevision','State'])
 OR NEW."Revision"<>OLD."Revision"+1 OR NEW."DataRevision" NOT BETWEEN OLD."DataRevision" AND OLD."DataRevision"+1
 THEN RAISE EXCEPTION 'Review provenance or revision invalid' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER review_guard BEFORE UPDATE OR DELETE ON "ProductionReviews" FOR EACH ROW EXECUTE FUNCTION sng_review_guard();
CREATE FUNCTION sng_review_row_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE parent "ProductionReviews";
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Review rows retained' USING ERRCODE='23514'; END IF;
 SELECT * INTO parent FROM "ProductionReviews" WHERE "Id"=NEW."ReviewId" FOR UPDATE;
 IF parent."State"<>1 THEN RAISE EXCEPTION 'Review frozen' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND (to_jsonb(NEW)-ARRAY['ValuesJson','Reviewed','Excluded']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['ValuesJson','Reviewed','Excluded'])
 THEN RAISE EXCEPTION 'Original row immutable' USING ERRCODE='23514'; END IF;
 IF NEW."CandidateId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProductionCandidateRows" c WHERE c."Id"=NEW."CandidateId" AND c."ComponentRevisionId"=parent."SourceRevisionId" AND c."StructureId"=NEW."StructureId" AND c."Op"=NEW."Op" AND c."DocumentId"=NEW."DocumentId" AND c."DocumentSha256"=NEW."DocumentSha256")
 THEN RAISE EXCEPTION 'Candidate origin mismatch' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER review_row_guard BEFORE INSERT OR UPDATE OR DELETE ON "ProductionReviewRows" FOR EACH ROW EXECUTE FUNCTION sng_review_row_guard();
CREATE FUNCTION sng_review_issue_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF TG_OP='DELETE' OR NOT EXISTS(SELECT 1 FROM "ProductionReviews" WHERE "Id"=NEW."ReviewId" AND "State"=1)
 THEN RAISE EXCEPTION 'Review issue frozen' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND (OLD."Category"<>'HumanReview' OR (to_jsonb(NEW)-'ResolvedDataRevision') IS DISTINCT FROM (to_jsonb(OLD)-'ResolvedDataRevision'))
 THEN RAISE EXCEPTION 'No resolution policy or issue origin altered' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE TRIGGER review_issue_guard BEFORE INSERT OR UPDATE OR DELETE ON "ProductionReviewIssues" FOR EACH ROW EXECUTE FUNCTION sng_review_issue_guard();
CREATE FUNCTION sng_review_approval_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
DECLARE rid uuid; aid "ProductionApprovals"; rev "ProductionReviews";
BEGIN
 IF TG_TABLE_NAME='ProductionReviews' THEN rid=NEW."Id"; ELSE rid=NEW."ReviewId"; END IF;
 SELECT * INTO rev FROM "ProductionReviews" WHERE "Id"=rid;
 SELECT * INTO aid FROM "ProductionApprovals" WHERE "ReviewId"=rid;
 IF rev."State"=2 THEN
  IF aid."Id" IS NULL OR aid."Revision"<>rev."Revision" OR aid."PreparationId"<>rev."PreparationId" OR upper(encode(sha256(convert_to(aid."Canonical",'UTF8')),'hex'))<>aid."Sha256"
  THEN RAISE EXCEPTION 'Approved snapshot missing or invalid' USING ERRCODE='23514'; END IF;
 ELSIF aid."Id" IS NOT NULL THEN RAISE EXCEPTION 'Review not approved' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $f$;
CREATE CONSTRAINT TRIGGER review_approval_consistent AFTER INSERT OR UPDATE ON "ProductionReviews" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_review_approval_guard();
CREATE CONSTRAINT TRIGGER approval_review_consistent AFTER INSERT ON "ProductionApprovals" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_review_approval_guard();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $guard$ BEGIN IF EXISTS(SELECT 1 FROM "ProductionReviews") OR EXISTS(SELECT 1 FROM "ProductionReviewAttachments") THEN
RAISE EXCEPTION 'Review evidence cannot be removed by rollback' USING ERRCODE='23514'; END IF; END $guard$;
DROP FUNCTION sng_review_approval_guard() CASCADE;
DROP FUNCTION sng_review_issue_guard() CASCADE;
DROP FUNCTION sng_review_row_guard() CASCADE;
DROP FUNCTION sng_review_guard() CASCADE;
DROP FUNCTION sng_review_job_frozen() CASCADE;
DROP FUNCTION sng_review_append_only() CASCADE;
""");
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentExtractionJobs_ProductionComponentRevisions_Compone~",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropTable(
                name: "ProductionApprovals");

            migrationBuilder.DropTable(
                name: "ProductionReviewAttachments");

            migrationBuilder.DropTable(
                name: "ProductionReviewEvents");

            migrationBuilder.DropTable(
                name: "ProductionReviewIssues");

            migrationBuilder.DropTable(
                name: "ProductionReviewRows");

            migrationBuilder.DropTable(
                name: "ProductionReviews");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductionCandidateRows_Id_PreparationId",
                table: "ProductionCandidateRows");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentExtractionJobs_Id_PreparationId",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropIndex(
                name: "IX_DocumentExtractionJobs_ComponentRevisionId_PreparationId",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropIndex(
                name: "IX_DocumentExtractionJobs_PreparationId_ExtractorVersion_Gener~",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropColumn(
                name: "ComponentRevisionId",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "DocumentExtractionJobs");

            migrationBuilder.DropColumn(
                name: "ReextractReason",
                table: "DocumentExtractionJobs");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractionJobs_PreparationId_ExtractorVersion",
                table: "DocumentExtractionJobs",
                columns: new[] { "PreparationId", "ExtractorVersion" },
                unique: true);
        }
    }
}
