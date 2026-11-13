using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnG.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableIncrementalCommercialOpenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialOpeningRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvisorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PresentedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstResponseAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningRounds", x => x.Id);
                    table.UniqueConstraint("AK_CommercialOpeningRounds_Id_OpeningRecordId_ProjectId", x => new { x.Id, x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_CommercialOpeningRound", "\"Number\">0 AND \"Revision\">=0 AND \"LastActivityAt\">=\"ReceivedAt\" AND (\"PresentedAt\" IS NULL OR \"PresentedAt\">=\"ReceivedAt\") AND (\"FirstResponseAt\" IS NULL OR (\"PresentedAt\" IS NOT NULL AND \"FirstResponseAt\">=\"PresentedAt\")) AND (\"ResolvedAt\" IS NULL OR (\"FirstResponseAt\" IS NOT NULL AND \"ResolvedAt\">=\"FirstResponseAt\"))");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRounds_OpeningDocumentScopes_OpeningRecord~",
                        columns: x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRounds_OpeningRoundLinks_Id_OpeningRecordI~",
                        columns: x => new { x.Id, x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningRoundLinks",
                        principalColumns: new[] { "RoundId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRounds_ProjectTasks_TaskId_ProjectId",
                        columns: x => new { x.TaskId, x.ProjectId },
                        principalTable: "ProjectTasks",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningEvents",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canonical = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningEvents", x => new { x.ProjectId, x.RequestKey });
                    table.ForeignKey(
                        name: "FK_CommercialOpeningEvents_CommercialOpeningRounds_RoundId_Ope~",
                        columns: x => new { x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRounds",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Declared = table.Column<bool>(type: "boolean", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningReviews", x => x.Id);
                    table.UniqueConstraint("AK_CommercialOpeningReviews_Id_RoundId_OpeningRecordId_Project~", x => new { x.Id, x.RoundId, x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_CommercialOpeningReview", "\"Revision\">=0 AND (\"CompletedAt\" IS NULL OR (\"CompletedAt\">=\"CreatedAt\" AND \"Declared\" AND \"DocumentId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningReviews_CommercialOpeningRounds_RoundId_Op~",
                        columns: x => new { x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRounds",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningReviews_OpeningDocumentScopes_OpeningRecor~",
                        columns: x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningReviews_ProjectTasks_TaskId_ProjectId",
                        columns: x => new { x.TaskId, x.ProjectId },
                        principalTable: "ProjectTasks",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningRows",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Approval = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningRows", x => new { x.RoundId, x.StructureId });
                    table.UniqueConstraint("AK_CommercialOpeningRows_RoundId_StructureId_OpeningRecordId_P~", x => new { x.RoundId, x.StructureId, x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_CommercialOpeningRow", "\"EvidenceVersion\">0 AND \"Revision\">=0 AND \"Approval\" IN ('AwaitingApproval','Confirmed','UnderEngineeringReview') AND ((\"Approval\"='Confirmed' AND \"AcceptanceId\" IS NOT NULL) OR (\"Approval\"<>'Confirmed' AND \"AcceptanceId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRows_CommercialOpeningRounds_RoundId_Openi~",
                        columns: x => new { x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRounds",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRows_OpeningAcceptances_AcceptanceId_Openi~",
                        columns: x => new { x.AcceptanceId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningAcceptances",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRows_OpeningDocumentScopes_OpeningRecordId~",
                        columns: x => new { x.OpeningRecordId, x.EvidenceDocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningRows_OpeningRecordMembers_OpeningRecordId_~",
                        columns: x => new { x.OpeningRecordId, x.StructureId, x.ProjectId },
                        principalTable: "OpeningRecordMembers",
                        principalColumns: new[] { "OpeningRecordId", "StructureId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeCanonical = table.Column<string>(type: "text", nullable: false),
                    ScopeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningDocuments", x => x.DocumentId);
                    table.UniqueConstraint("AK_CommercialOpeningDocuments_DocumentId_RoundId_OpeningRecord~", x => new { x.DocumentId, x.RoundId, x.OpeningRecordId, x.ProjectId });
                    table.CheckConstraint("CK_CommercialOpeningDocument", "\"Kind\" IN ('SIGNED','REVISED') AND ((\"Kind\"='REVISED' AND \"ReviewId\" IS NOT NULL) OR (\"Kind\"='SIGNED' AND \"ReviewId\" IS NULL)) AND \"ScopeHash\" ~ '^[A-F0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocuments_CommercialOpeningReviews_ReviewI~",
                        columns: x => new { x.ReviewId, x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningReviews",
                        principalColumns: new[] { "Id", "RoundId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocuments_CommercialOpeningRounds_RoundId_~",
                        columns: x => new { x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRounds",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocuments_OpeningDocumentScopes_OpeningRec~",
                        columns: x => new { x.OpeningRecordId, x.DocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningReviewMembers",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    Requested = table.Column<bool>(type: "boolean", nullable: false),
                    Changed = table.Column<bool>(type: "boolean", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningReviewMembers", x => new { x.ReviewId, x.StructureId });
                    table.CheckConstraint("CK_CommercialOpeningReviewMember", "\"Revision\">=0 AND \"EvidenceVersion\">0 AND (NOT \"Completed\" OR (\"ChangedAt\" IS NOT NULL AND \"ChangedBySubject\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningReviewMembers_CommercialOpeningReviews_Rev~",
                        columns: x => new { x.ReviewId, x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningReviews",
                        principalColumns: new[] { "Id", "RoundId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningReviewMembers_CommercialOpeningRows_RoundI~",
                        columns: x => new { x.RoundId, x.StructureId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRows",
                        principalColumns: new[] { "RoundId", "StructureId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningDocumentMembers",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningDocumentMembers", x => new { x.DocumentId, x.StructureId });
                    table.UniqueConstraint("AK_CommercialOpeningDocumentMembers_DocumentId_StructureId_Rou~", x => new { x.DocumentId, x.StructureId, x.RoundId, x.OpeningRecordId, x.ProjectId, x.EvidenceVersion, x.EvidenceDocumentId });
                    table.CheckConstraint("CK_CommercialOpeningDocumentMember", "\"EvidenceVersion\">0");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocumentMembers_CommercialOpeningDocuments~",
                        columns: x => new { x.DocumentId, x.RoundId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningDocuments",
                        principalColumns: new[] { "DocumentId", "RoundId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocumentMembers_CommercialOpeningRows_Roun~",
                        columns: x => new { x.RoundId, x.StructureId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "CommercialOpeningRows",
                        principalColumns: new[] { "RoundId", "StructureId", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningDocumentMembers_OpeningDocumentScopes_Open~",
                        columns: x => new { x.OpeningRecordId, x.EvidenceDocumentId, x.ProjectId },
                        principalTable: "OpeningDocumentScopes",
                        principalColumns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpeningAcceptanceEvidence",
                columns: table => new
                {
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "integer", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpeningAcceptanceEvidence", x => new { x.AcceptanceId, x.StructureId });
                    table.CheckConstraint("CK_CommercialOpeningAcceptanceEvidence", "\"Attested\" AND \"EvidenceVersion\">0");
                    table.ForeignKey(
                        name: "FK_CommercialOpeningAcceptanceEvidence_CommercialOpeningDocume~",
                        columns: x => new { x.SignedDocumentId, x.StructureId, x.RoundId, x.OpeningRecordId, x.ProjectId, x.EvidenceVersion, x.EvidenceDocumentId },
                        principalTable: "CommercialOpeningDocumentMembers",
                        principalColumns: new[] { "DocumentId", "StructureId", "RoundId", "OpeningRecordId", "ProjectId", "EvidenceVersion", "EvidenceDocumentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningAcceptanceEvidence_OpeningAcceptanceMember~",
                        columns: x => new { x.AcceptanceId, x.StructureId },
                        principalTable: "OpeningAcceptanceMembers",
                        principalColumns: new[] { "AcceptanceId", "StructureId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialOpeningAcceptanceEvidence_OpeningAcceptances_Acce~",
                        columns: x => new { x.AcceptanceId, x.OpeningRecordId, x.ProjectId },
                        principalTable: "OpeningAcceptances",
                        principalColumns: new[] { "Id", "OpeningRecordId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningAcceptanceEvidence_AcceptanceId_OpeningRec~",
                table: "CommercialOpeningAcceptanceEvidence",
                columns: new[] { "AcceptanceId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningAcceptanceEvidence_SignedDocumentId_Struct~",
                table: "CommercialOpeningAcceptanceEvidence",
                columns: new[] { "SignedDocumentId", "StructureId", "RoundId", "OpeningRecordId", "ProjectId", "EvidenceVersion", "EvidenceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocumentMembers_DocumentId_RoundId_Opening~",
                table: "CommercialOpeningDocumentMembers",
                columns: new[] { "DocumentId", "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocumentMembers_OpeningRecordId_EvidenceDo~",
                table: "CommercialOpeningDocumentMembers",
                columns: new[] { "OpeningRecordId", "EvidenceDocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocumentMembers_RoundId_StructureId_Openin~",
                table: "CommercialOpeningDocumentMembers",
                columns: new[] { "RoundId", "StructureId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocuments_OpeningRecordId_DocumentId_Proje~",
                table: "CommercialOpeningDocuments",
                columns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocuments_ReviewId_RoundId_OpeningRecordId~",
                table: "CommercialOpeningDocuments",
                columns: new[] { "ReviewId", "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningDocuments_RoundId_OpeningRecordId_ProjectId",
                table: "CommercialOpeningDocuments",
                columns: new[] { "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningEvents_RoundId_OpeningRecordId_ProjectId",
                table: "CommercialOpeningEvents",
                columns: new[] { "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviewMembers_ReviewId_RoundId_OpeningReco~",
                table: "CommercialOpeningReviewMembers",
                columns: new[] { "ReviewId", "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviewMembers_RoundId_StructureId_OpeningR~",
                table: "CommercialOpeningReviewMembers",
                columns: new[] { "RoundId", "StructureId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviews_OpeningRecordId",
                table: "CommercialOpeningReviews",
                column: "OpeningRecordId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviews_OpeningRecordId_DocumentId_Project~",
                table: "CommercialOpeningReviews",
                columns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviews_RoundId_OpeningRecordId_ProjectId",
                table: "CommercialOpeningReviews",
                columns: new[] { "RoundId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviews_TaskId",
                table: "CommercialOpeningReviews",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningReviews_TaskId_ProjectId",
                table: "CommercialOpeningReviews",
                columns: new[] { "TaskId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRounds_OpeningRecordId",
                table: "CommercialOpeningRounds",
                column: "OpeningRecordId",
                unique: true,
                filter: "\"SupersededAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRounds_OpeningRecordId_DocumentId_ProjectId",
                table: "CommercialOpeningRounds",
                columns: new[] { "OpeningRecordId", "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRounds_OpeningRecordId_Number",
                table: "CommercialOpeningRounds",
                columns: new[] { "OpeningRecordId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRounds_TaskId",
                table: "CommercialOpeningRounds",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRounds_TaskId_ProjectId",
                table: "CommercialOpeningRounds",
                columns: new[] { "TaskId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRows_AcceptanceId_OpeningRecordId_ProjectId",
                table: "CommercialOpeningRows",
                columns: new[] { "AcceptanceId", "OpeningRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRows_OpeningRecordId_EvidenceDocumentId_Pr~",
                table: "CommercialOpeningRows",
                columns: new[] { "OpeningRecordId", "EvidenceDocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRows_OpeningRecordId_StructureId_ProjectId",
                table: "CommercialOpeningRows",
                columns: new[] { "OpeningRecordId", "StructureId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpeningRows_RoundId_OpeningRecordId_ProjectId",
                table: "CommercialOpeningRows",
                columns: new[] { "RoundId", "OpeningRecordId", "ProjectId" });
            migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION sng_engineering_confirmed_guard() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE acta uuid; project uuid; structure uuid;
                BEGIN
                  IF TG_TABLE_NAME='EngineeringOpeningStates' THEN
                    IF TG_OP='DELETE' OR OLD."ConfirmedAt" IS NOT NULL THEN RAISE EXCEPTION 'Confirmed engineering evidence is immutable' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                  ELSIF TG_TABLE_NAME IN ('OpeningRecordMembers','OpeningDocumentScopes','OpeningDocumentMembers') THEN
                    acta:=NEW."OpeningRecordId";
                    IF EXISTS(SELECT 1 FROM "EngineeringOpeningStates" WHERE "OpeningRecordId"=acta AND "ConfirmedAt" IS NOT NULL) THEN
                      IF TG_TABLE_NAME='OpeningDocumentScopes' THEN
                        IF NEW."Kind" IN ('SIGNED','REVISED') THEN RETURN NEW; END IF;
                      ELSIF TG_TABLE_NAME='OpeningDocumentMembers' THEN
                        IF EXISTS(SELECT 1 FROM "OpeningDocumentScopes" d WHERE d."OpeningRecordId"=acta AND d."DocumentId"=NEW."DocumentId" AND d."Kind" IN ('SIGNED','REVISED')) THEN RETURN NEW; END IF;
                      END IF;
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

ALTER TABLE "CommercialOpeningRounds" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;
CREATE FUNCTION sng_commercial_opening_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE permitted boolean;
BEGIN
 IF TG_TABLE_NAME='CommercialOpeningRows' THEN
   IF TG_OP='INSERT' THEN
     SELECT "CreationTransactionId"=pg_current_xact_id()::text INTO permitted FROM "CommercialOpeningRounds" WHERE "Id"=NEW."RoundId";
     IF permitted IS DISTINCT FROM true THEN RAISE EXCEPTION 'Frozen round manifest' USING ERRCODE='23514'; END IF;
   ELSE
     IF TG_OP='DELETE' OR EXISTS(SELECT 1 FROM "CommercialOpeningRounds" WHERE "Id"=OLD."RoundId" AND ("SupersededAt" IS NOT NULL OR "ResolvedAt" IS NOT NULL))
        OR (NEW."RoundId",NEW."StructureId",NEW."ProjectId",NEW."OpeningRecordId",NEW."EvidenceVersion",NEW."EvidenceDocumentId") IS DISTINCT FROM (OLD."RoundId",OLD."StructureId",OLD."ProjectId",OLD."OpeningRecordId",OLD."EvidenceVersion",OLD."EvidenceDocumentId") THEN
       RAISE EXCEPTION 'Frozen round evidence' USING ERRCODE='23514';
     END IF;
   END IF;
 ELSIF TG_TABLE_NAME='CommercialOpeningReviewMembers' THEN
   IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Review history is immutable' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM "CommercialOpeningReviews" WHERE "Id"=NEW."ReviewId" AND "CompletedAt" IS NOT NULL) THEN RAISE EXCEPTION 'Closed review' USING ERRCODE='23514'; END IF;
   IF TG_OP='UPDATE' AND (NEW."ReviewId",NEW."StructureId",NEW."EvidenceVersion",NEW."RoundId",NEW."ProjectId",NEW."OpeningRecordId") IS DISTINCT FROM (OLD."ReviewId",OLD."StructureId",OLD."EvidenceVersion",OLD."RoundId",OLD."ProjectId",OLD."OpeningRecordId") THEN RAISE EXCEPTION 'Frozen review evidence' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='CommercialOpeningRounds' THEN
   IF TG_OP='DELETE' OR OLD."SupersededAt" IS NOT NULL OR OLD."ResolvedAt" IS NOT NULL OR
      (NEW."Id",NEW."ProjectId",NEW."OpeningRecordId",NEW."Number",NEW."TaskId",NEW."DocumentId",NEW."AdvisorSubject",NEW."ReceivedAt") IS DISTINCT FROM
      (OLD."Id",OLD."ProjectId",OLD."OpeningRecordId",OLD."Number",OLD."TaskId",OLD."DocumentId",OLD."AdvisorSubject",OLD."ReceivedAt") THEN RAISE EXCEPTION 'Frozen round history' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='CommercialOpeningReviews' THEN
   IF TG_OP='DELETE' OR OLD."CompletedAt" IS NOT NULL OR (NEW."Id",NEW."ProjectId",NEW."OpeningRecordId",NEW."RoundId",NEW."TaskId",NEW."Subject",NEW."CreatedAt") IS DISTINCT FROM (OLD."Id",OLD."ProjectId",OLD."OpeningRecordId",OLD."RoundId",OLD."TaskId",OLD."Subject",OLD."CreatedAt") THEN RAISE EXCEPTION 'Frozen review history' USING ERRCODE='23514'; END IF;
 ELSE
   SELECT d."CreationTransactionId"=pg_current_xact_id()::text INTO permitted FROM "OpeningDocumentScopes" d WHERE d."OpeningRecordId"=NEW."OpeningRecordId" AND d."DocumentId"=NEW."DocumentId";
   IF permitted IS DISTINCT FROM true THEN RAISE EXCEPTION 'Frozen document membership' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END; $fn$;
CREATE TRIGGER commercial_row_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommercialOpeningRows" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_review_member_guard BEFORE INSERT OR UPDATE OR DELETE ON "CommercialOpeningReviewMembers" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_round_guard BEFORE UPDATE OR DELETE ON "CommercialOpeningRounds" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_review_guard BEFORE UPDATE OR DELETE ON "CommercialOpeningReviews" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_document_member_guard BEFORE INSERT ON "CommercialOpeningDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_document_guard BEFORE INSERT ON "CommercialOpeningDocuments" FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_guard();
CREATE TRIGGER commercial_frozen BEFORE UPDATE OR DELETE ON "CommercialOpeningDocuments" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER commercial_frozen BEFORE UPDATE OR DELETE ON "CommercialOpeningDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER commercial_frozen BEFORE UPDATE OR DELETE ON "CommercialOpeningEvents" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER commercial_frozen BEFORE UPDATE OR DELETE ON "CommercialOpeningAcceptanceEvidence" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE FUNCTION sng_commercial_opening_integrity() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
 IF TG_TABLE_NAME='CommercialOpeningRounds' THEN
   IF NOT EXISTS(SELECT 1 FROM "EngineeringOpeningStates" s WHERE s."OpeningRecordId"=NEW."OpeningRecordId" AND s."ProjectId"=NEW."ProjectId" AND s."ConfirmedAt" IS NOT NULL)
     OR NOT EXISTS(SELECT 1 FROM "CommercialOpeningRows" r WHERE r."RoundId"=NEW."Id")
     OR (SELECT count(*) FROM "CommercialOpeningRows" r WHERE r."RoundId"=NEW."Id")<>(SELECT count(*) FROM "OpeningRecordMembers" m WHERE m."OpeningRecordId"=NEW."OpeningRecordId")
     OR NOT EXISTS(SELECT 1 FROM "ProjectTasks" t WHERE t."Id"=NEW."TaskId" AND t."ProjectId"=NEW."ProjectId" AND t."Type"=8 AND t."AssignedToGoogleSubject"=NEW."AdvisorSubject") THEN RAISE EXCEPTION 'Complete native round required' USING ERRCODE='23514'; END IF;
   IF NEW."Number"=1 THEN
     IF NOT EXISTS(SELECT 1 FROM "EngineeringOpeningStates" s WHERE s."OpeningRecordId"=NEW."OpeningRecordId" AND s."CurrentDocumentId"=NEW."DocumentId") OR EXISTS(SELECT 1 FROM "CommercialOpeningRows" r WHERE r."RoundId"=NEW."Id" AND (r."EvidenceVersion"<>1 OR r."EvidenceDocumentId"<>NEW."DocumentId")) THEN RAISE EXCEPTION 'Initial round source mismatch' USING ERRCODE='23514'; END IF;
   ELSE
     IF NOT EXISTS(SELECT 1 FROM "CommercialOpeningDocuments" d JOIN "CommercialOpeningReviews" v ON v."Id"=d."ReviewId" JOIN "CommercialOpeningRounds" p ON p."Id"=v."RoundId"
       WHERE d."DocumentId"=NEW."DocumentId" AND d."Kind"='REVISED' AND d."OpeningRecordId"=NEW."OpeningRecordId" AND v."CompletedAt" IS NOT NULL AND p."Number"=NEW."Number"-1 AND p."SupersededAt" IS NOT NULL)
       OR EXISTS(SELECT 1 FROM "CommercialOpeningRows" r LEFT JOIN "CommercialOpeningDocumentMembers" m ON m."DocumentId"=NEW."DocumentId" AND m."StructureId"=r."StructureId" WHERE r."RoundId"=NEW."Id" AND (m."StructureId" IS NULL OR m."EvidenceVersion"<>r."EvidenceVersion" OR m."EvidenceDocumentId"<>r."EvidenceDocumentId")) THEN RAISE EXCEPTION 'Revised round source mismatch' USING ERRCODE='23514'; END IF;
   END IF;
 ELSIF TG_TABLE_NAME='OpeningDocumentScopes' THEN
   IF NEW."Kind" IN ('SIGNED','REVISED') AND EXISTS(SELECT 1 FROM "EngineeringOpeningStates" s WHERE s."OpeningRecordId"=NEW."OpeningRecordId" AND s."ConfirmedAt" IS NOT NULL) AND NOT EXISTS(SELECT 1 FROM "CommercialOpeningDocuments" d WHERE d."DocumentId"=NEW."DocumentId" AND d."OpeningRecordId"=NEW."OpeningRecordId" AND d."ProjectId"=NEW."ProjectId" AND d."Kind"=NEW."Kind") THEN RAISE EXCEPTION 'Explicit native document binding required' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='CommercialOpeningDocuments' THEN
   IF NOT EXISTS(SELECT 1 FROM "CommercialOpeningDocumentMembers" m WHERE m."DocumentId"=NEW."DocumentId") OR EXISTS(
     SELECT 1 FROM "CommercialOpeningDocumentMembers" m LEFT JOIN "OpeningDocumentMembers" o ON o."OpeningRecordId"=m."OpeningRecordId" AND o."DocumentId"=m."DocumentId" AND o."StructureId"=m."StructureId" WHERE m."DocumentId"=NEW."DocumentId" AND o."StructureId" IS NULL)
     OR (SELECT count(*) FROM "OpeningDocumentMembers" m WHERE m."OpeningRecordId"=NEW."OpeningRecordId" AND m."DocumentId"=NEW."DocumentId")<>(SELECT count(*) FROM "CommercialOpeningDocumentMembers" m WHERE m."DocumentId"=NEW."DocumentId") THEN RAISE EXCEPTION 'Declared document scope required' USING ERRCODE='23514'; END IF;
   IF NEW."Kind"='SIGNED' AND EXISTS(SELECT 1 FROM "CommercialOpeningDocumentMembers" m JOIN "CommercialOpeningRows" r ON r."RoundId"=m."RoundId" AND r."StructureId"=m."StructureId" WHERE m."DocumentId"=NEW."DocumentId" AND (m."EvidenceVersion"<>r."EvidenceVersion" OR m."EvidenceDocumentId"<>r."EvidenceDocumentId" OR r."Approval"<>'AwaitingApproval')) THEN RAISE EXCEPTION 'Signed scope uses stale evidence' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='CommercialOpeningRows' THEN
   IF NEW."Approval"='Confirmed' AND NOT EXISTS(SELECT 1 FROM "CommercialOpeningAcceptanceEvidence" e WHERE e."AcceptanceId"=NEW."AcceptanceId" AND e."StructureId"=NEW."StructureId" AND e."ProjectId"=NEW."ProjectId" AND e."OpeningRecordId"=NEW."OpeningRecordId" AND e."EvidenceVersion"=NEW."EvidenceVersion" AND e."EvidenceDocumentId"=NEW."EvidenceDocumentId") THEN RAISE EXCEPTION 'Approval requires matching accepted evidence' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='OpeningAcceptances' THEN
   IF NOT NEW."IsLegacy" THEN
     IF NOT EXISTS(SELECT 1 FROM "CommercialOpeningDocuments" d WHERE d."DocumentId"=NEW."SignedDocumentId" AND d."RoundId"=NEW."RoundId" AND d."OpeningRecordId"=NEW."OpeningRecordId" AND d."ProjectId"=NEW."ProjectId" AND d."Kind"='SIGNED')
       OR EXISTS(SELECT 1 FROM "OpeningAcceptanceMembers" m LEFT JOIN "CommercialOpeningAcceptanceEvidence" e ON e."AcceptanceId"=m."AcceptanceId" AND e."StructureId"=m."StructureId" WHERE m."AcceptanceId"=NEW."Id" AND e."StructureId" IS NULL)
       OR EXISTS(SELECT 1 FROM "CommercialOpeningAcceptanceEvidence" e LEFT JOIN "CommercialOpeningRows" r ON r."RoundId"=e."RoundId" AND r."StructureId"=e."StructureId" WHERE e."AcceptanceId"=NEW."Id" AND (r."AcceptanceId" IS DISTINCT FROM NEW."Id" OR r."Approval"<>'Confirmed' OR r."EvidenceVersion"<>e."EvidenceVersion" OR r."EvidenceDocumentId"<>e."EvidenceDocumentId" OR e."SignedDocumentId"<>NEW."SignedDocumentId" OR e."RoundId"<>NEW."RoundId" OR NOT e."Attested")) THEN RAISE EXCEPTION 'Signed acceptance evidence mismatch' USING ERRCODE='23514'; END IF;
   END IF;
 END IF;
 RETURN NULL;
END; $fn$;
CREATE CONSTRAINT TRIGGER commercial_round_integrity AFTER INSERT ON "CommercialOpeningRounds" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_integrity();
CREATE CONSTRAINT TRIGGER commercial_document_scope_integrity AFTER INSERT ON "OpeningDocumentScopes" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_integrity();
CREATE CONSTRAINT TRIGGER commercial_document_integrity AFTER INSERT ON "CommercialOpeningDocuments" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_integrity();
CREATE CONSTRAINT TRIGGER commercial_approval_integrity AFTER INSERT OR UPDATE ON "CommercialOpeningRows" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_integrity();
CREATE CONSTRAINT TRIGGER commercial_acceptance_integrity AFTER INSERT ON "OpeningAcceptances" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_commercial_opening_integrity();

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""

DO $$ BEGIN IF EXISTS(SELECT 1 FROM "CommercialOpeningRounds") THEN RAISE EXCEPTION 'Native commercial evidence forbids downgrade' USING ERRCODE='23514'; END IF; END $$;
DROP TRIGGER commercial_document_scope_integrity ON "OpeningDocumentScopes";
DROP TRIGGER commercial_acceptance_integrity ON "OpeningAcceptances";
CREATE OR REPLACE FUNCTION sng_engineering_confirmed_guard() RETURNS trigger LANGUAGE plpgsql AS $body$
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
""");
            migrationBuilder.DropTable(
                name: "CommercialOpeningAcceptanceEvidence");

            migrationBuilder.DropTable(
                name: "CommercialOpeningEvents");

            migrationBuilder.DropTable(
                name: "CommercialOpeningReviewMembers");

            migrationBuilder.DropTable(
                name: "CommercialOpeningDocumentMembers");

            migrationBuilder.DropTable(
                name: "CommercialOpeningDocuments");

            migrationBuilder.DropTable(
                name: "CommercialOpeningRows");

            migrationBuilder.DropTable(
                name: "CommercialOpeningReviews");

            migrationBuilder.DropTable(
                name: "CommercialOpeningRounds");
            migrationBuilder.Sql("DROP FUNCTION sng_commercial_opening_guard(); DROP FUNCTION sng_commercial_opening_integrity();");
        }
    }
}
