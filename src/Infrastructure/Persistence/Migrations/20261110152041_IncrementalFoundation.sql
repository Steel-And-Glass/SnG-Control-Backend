ALTER TABLE "MeasurementSubmissions" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;
ALTER TABLE "MeasurementAmendments" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;
ALTER TABLE "SubmissionDocumentScopes" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;
ALTER TABLE "OpeningAcceptances" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;
ALTER TABLE "OpeningDocumentScopes" ADD COLUMN "CreationTransactionId" text NOT NULL DEFAULT pg_current_xact_id()::text;

-- Compatibility projection, not an incremental workflow. Reads frozen legacy evidence only.
-- Classification is insert-once: later changes to mutable source rows never promote incomplete evidence.
CREATE FUNCTION sng_incremental_legacy_project(p_project uuid) RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE legacy_check record; report record; payload jsonb; canonical text; issue text; availability text; status text;
BEGIN
  FOR legacy_check IN SELECT * FROM "MeasurementChecklists" WHERE "ProjectId"=p_project AND "SubmissionId" IS NOT NULL LOOP
    IF NOT EXISTS (SELECT 1 FROM "MeasurementSubmissions" WHERE "Id"=legacy_check."SubmissionId") THEN
      SELECT * INTO report FROM "MeasurementReports" WHERE "ChecklistId"=legacy_check."Id";
      availability := CASE WHEN report."ChecklistId" IS NULL THEN 'ABSENT' ELSE 'PRESENT' END;
      canonical := COALESCE(report."SnapshotCanonical", legacy_check."SnapshotJson"::text);
      payload := legacy_check."SnapshotJson"; issue := NULL;
      IF NOT (payload ? 'Structures') THEN issue := 'MISSING_MEMBERS';
      ELSIF jsonb_typeof(payload->'Structures') <> 'array' THEN issue := 'MALFORMED_MEMBERS';
      ELSIF jsonb_array_length(payload->'Structures')=0 THEN issue := 'EMPTY_SCOPE';
      ELSE
        IF EXISTS (SELECT 1 FROM jsonb_array_elements(payload->'Structures') s WHERE
          jsonb_typeof(s)<>'object' OR COALESCE(s->>'Id','') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
          OR NOT (s ? 'RetiredAt'))
        THEN issue := 'INVALID_MEMBER_ID_OR_STATE';
        ELSIF EXISTS (SELECT 1 FROM jsonb_array_elements(payload->'Structures') s GROUP BY s->>'Id' HAVING count(*)>1)
        THEN issue := 'DUPLICATE_MEMBERS';
        ELSIF EXISTS (SELECT 1 FROM jsonb_array_elements(payload->'Structures') s WHERE NOT EXISTS
          (SELECT 1 FROM "MeasurementStructures" m WHERE m."Id"=(s->>'Id')::uuid AND m."ProjectId"=p_project AND m."ChecklistId"=legacy_check."Id"))
        THEN issue := 'UNRESOLVED_MEMBER_IDENTITY';
        ELSIF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(payload->'Structures') s WHERE s->'RetiredAt'='null'::jsonb)
        THEN issue := 'EMPTY_ACTIVE_SCOPE';
        END IF;
      END IF;
      IF issue IS NULL THEN
        IF jsonb_typeof(payload->'DocumentIds') IS DISTINCT FROM 'array' THEN issue := 'MISSING_DOCUMENT_SCOPE';
        ELSIF EXISTS (SELECT 1 FROM jsonb_array_elements_text(payload->'DocumentIds') d
          WHERE d !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
        THEN issue := 'INVALID_DOCUMENT_SCOPE';
        ELSIF EXISTS (SELECT 1 FROM jsonb_array_elements_text(payload->'DocumentIds') d
          WHERE NOT EXISTS (SELECT 1 FROM "MeasurementSubmissionDocuments" link JOIN "ProjectDocuments" pd ON pd."Id"=link."ProjectDocumentId"
            WHERE link."ChecklistId"=legacy_check."Id" AND pd."ProjectId"=p_project AND pd."Id"=d::uuid))
          OR EXISTS (SELECT 1 FROM "MeasurementSubmissionDocuments" link WHERE link."ChecklistId"=legacy_check."Id"
            AND NOT (payload->'DocumentIds' @> jsonb_build_array(link."ProjectDocumentId"::text)))
        THEN issue := 'UNRESOLVED_DOCUMENT_SCOPE';
        END IF;
      END IF;
      IF report."ChecklistId" IS NOT NULL THEN
        BEGIN
          IF report."SnapshotCanonical"::jsonb IS DISTINCT FROM payload THEN issue := 'REPORT_SNAPSHOT_MISMATCH'; END IF;
        EXCEPTION WHEN invalid_text_representation THEN issue := 'REPORT_SNAPSHOT_UNREADABLE';
        END;
        IF FALSE
          OR upper(report."SnapshotSha256")<>upper(encode(sha256(convert_to(canonical,'UTF8')),'hex'))
          OR upper(report."PdfSha256")<>upper(encode(sha256(report."Pdf"),'hex'))
        THEN issue := 'REPORT_INTEGRITY_MISMATCH'; END IF;
      END IF;
      status := CASE WHEN issue IS NULL THEN 'COMPLETE' ELSE 'LEGACY_INCOMPLETE_EVIDENCE' END;
      INSERT INTO "MeasurementSubmissions" ("Id","ProjectId","ChecklistId","Number","SubmittedAt","SubmittedBySubject","EvidenceStatus","ScopeIssue","ReportAvailability","SnapshotCanonical","SnapshotSha256","IsLegacy")
      VALUES(legacy_check."SubmissionId",p_project,legacy_check."Id",1,legacy_check."SubmittedAt",legacy_check."SubmittedBySubject",status,issue,availability,canonical,upper(encode(sha256(convert_to(canonical,'UTF8')),'hex')),true);
      IF status='COMPLETE' THEN
        INSERT INTO "MeasurementSubmissionMembers" ("SubmissionId","ProjectId","StructureId","EvidenceVersion","AmendmentVersion","Position","SnapshotCanonical","SnapshotSha256")
        SELECT legacy_check."SubmissionId",p_project,(s->>'Id')::uuid,1,NULL,(ordinality-1)::int,s::text,upper(encode(sha256(convert_to(s::text,'UTF8')),'hex'))
        FROM jsonb_array_elements(payload->'Structures') WITH ORDINALITY a(s,ordinality) WHERE s->'RetiredAt'='null'::jsonb;
        INSERT INTO "SubmissionDocumentScopes" ("SubmissionId","ProjectId","DocumentId","Declaration")
        SELECT legacy_check."SubmissionId",p_project,d::uuid,'LEGACY_FROZEN_SUBMISSION_SCOPE'
        FROM jsonb_array_elements_text(payload->'DocumentIds') d;
        INSERT INTO "SubmissionDocumentMembers" ("SubmissionId","ProjectId","DocumentId","StructureId","EvidenceVersion")
        SELECT s."SubmissionId",p_project,s."DocumentId",m."StructureId",m."EvidenceVersion"
        FROM "SubmissionDocumentScopes" s JOIN "MeasurementSubmissionMembers" m ON m."SubmissionId"=s."SubmissionId"
        WHERE s."SubmissionId"=legacy_check."SubmissionId";
      END IF;
      IF report."ChecklistId" IS NOT NULL THEN
        INSERT INTO "SubmissionReports" ("SubmissionId","ProjectId","Version","LegacyChecklistId","PdfSha256","SnapshotSha256")
        VALUES(legacy_check."SubmissionId",p_project,report."Version",report."ChecklistId",report."PdfSha256",report."SnapshotSha256");
      END IF;
    END IF;
  END LOOP;

  INSERT INTO "EngineeringWorkspaces" ("ProjectId","CreatedAt")
  SELECT "ProjectId",min("CreatedAt") FROM "EngineeringChecklists" WHERE "ProjectId"=p_project GROUP BY "ProjectId" ON CONFLICT DO NOTHING;
  INSERT INTO "EngineeringReceipts" ("ProjectId","SubmissionId","ReceivedAt")
  SELECT e."ProjectId",e."SubmissionId",e."CreatedAt" FROM "EngineeringChecklists" e JOIN "MeasurementSubmissions" s ON s."Id"=e."SubmissionId"
  WHERE e."ProjectId"=p_project AND s."EvidenceStatus"='COMPLETE' ON CONFLICT DO NOTHING;
  INSERT INTO "EngineeringEvidenceChecks" ("ProjectId","SubmissionId","StructureId","EvidenceVersion","Completed","Revision","ChangedAt","ChangedBySubject","LegacyChecklistId")
  SELECT e."ProjectId",c."SubmissionId",e."StructureId",1,e."Completed",e."Revision",e."ChangedAt",e."ChangedBySubject",e."ChecklistId"
  FROM "EngineeringStructures" e JOIN "EngineeringChecklists" c ON c."Id"=e."ChecklistId"
  JOIN "MeasurementSubmissionMembers" m ON m."SubmissionId"=c."SubmissionId" AND m."StructureId"=e."StructureId" AND m."EvidenceVersion"=1
  WHERE e."ProjectId"=p_project
  ON CONFLICT ("ProjectId","SubmissionId","StructureId","EvidenceVersion") DO UPDATE
  SET "Completed"=EXCLUDED."Completed","Revision"=EXCLUDED."Revision","ChangedAt"=EXCLUDED."ChangedAt","ChangedBySubject"=EXCLUDED."ChangedBySubject"
  WHERE "EngineeringEvidenceChecks"."LegacyChecklistId"=EXCLUDED."LegacyChecklistId";

  INSERT INTO "WorkPackageAssignments" ("Id","ProjectId","PackageId","Area","Subject","AssignedAt","EndedAt","LegacyTaskId")
  SELECT t."Id",t."ProjectId",t."Id",t."Area",t."AssignedToGoogleSubject",t."AssignedAt",t."CompletedAt",t."Id"
  FROM "ProjectTasks" t WHERE t."ProjectId"=p_project AND t."Type" IN (5,7,8,9,10) AND nullif(btrim(t."AssignedToGoogleSubject"),'') IS NOT NULL
  ON CONFLICT ("Id") DO UPDATE SET "EndedAt"=EXCLUDED."EndedAt" WHERE "WorkPackageAssignments"."LegacyTaskId"=EXCLUDED."LegacyTaskId";
  INSERT INTO "WorkPackageMembers" ("AssignmentId","ProjectId","SubmissionId","StructureId","EvidenceVersion")
  SELECT a."Id",p_project,m."SubmissionId",m."StructureId",m."EvidenceVersion"
  FROM "WorkPackageAssignments" a JOIN "ProjectTasks" t ON t."Id"=a."LegacyTaskId"
  JOIN "MeasurementSubmissions" s ON s."ProjectId"=a."ProjectId"
  JOIN "MeasurementSubmissionMembers" m ON m."SubmissionId"=s."Id" AND m."EvidenceVersion"=1
  WHERE a."ProjectId"=p_project AND t."Type" IN (5,7) ON CONFLICT DO NOTHING;

  INSERT INTO "OpeningRecords" ("Id","ProjectId","Number","CreatedAt","LegacyChecklistId","EvidenceStatus")
  SELECT e."Id",e."ProjectId",1,e."CreatedAt",e."Id",
    CASE WHEN s."EvidenceStatus"='COMPLETE'
      AND EXISTS (SELECT 1 FROM "EngineeringStructures" r WHERE r."ChecklistId"=e."Id")
      AND NOT EXISTS (SELECT 1 FROM "EngineeringStructures" r WHERE r."ChecklistId"=e."Id" AND NOT EXISTS
        (SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=e."SubmissionId" AND m."StructureId"=r."StructureId" AND m."EvidenceVersion"=1))
    THEN 'COMPLETE' ELSE 'LEGACY_INCOMPLETE_EVIDENCE' END
  FROM "EngineeringChecklists" e JOIN "MeasurementSubmissions" s ON s."Id"=e."SubmissionId" WHERE e."ProjectId"=p_project ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId","SubmissionId","EvidenceVersion","Position")
  SELECT o."Id",p_project,r."StructureId",e."SubmissionId",1,r."Position"
  FROM "OpeningRecords" o JOIN "EngineeringChecklists" e ON e."Id"=o."LegacyChecklistId"
  JOIN "EngineeringStructures" r ON r."ChecklistId"=e."Id"
  WHERE o."ProjectId"=p_project AND o."EvidenceStatus"='COMPLETE' ON CONFLICT DO NOTHING;
  INSERT INTO "ActiveOpeningRecordMembers" ("OpeningRecordId","ProjectId","StructureId")
  SELECT m."OpeningRecordId",p_project,m."StructureId" FROM "OpeningRecordMembers" m JOIN "OpeningRecords" o ON o."Id"=m."OpeningRecordId"
  WHERE m."ProjectId"=p_project AND o."LegacyChecklistId" IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM "OpeningRecordRounds" r WHERE r."ChecklistId"=o."LegacyChecklistId" AND r."Status"=5)
  ON CONFLICT DO NOTHING;
  DELETE FROM "ActiveOpeningRecordMembers" a USING "OpeningRecords" o
  WHERE a."OpeningRecordId"=o."Id" AND o."ProjectId"=p_project AND o."LegacyChecklistId" IS NOT NULL
    AND EXISTS (SELECT 1 FROM "OpeningRecordRounds" r WHERE r."ChecklistId"=o."LegacyChecklistId" AND r."Status"=5);

  INSERT INTO "OpeningRoundLinks" ("RoundId","ProjectId","OpeningRecordId","Number","LegacyRoundId")
  SELECT r."Id",p_project,o."Id",r."Number",r."Id" FROM "OpeningRecordRounds" r JOIN "OpeningRecords" o ON o."LegacyChecklistId"=r."ChecklistId"
  WHERE r."ProjectId"=p_project ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningDocumentScopes" ("OpeningRecordId","ProjectId","DocumentId","Kind","Version","Sha256","Declaration")
  SELECT o."Id",p_project,a."DocumentId",'ENGINEERING',a."Version",a."Sha256",'LEGACY_ENGINEERING_SCOPE'
  FROM "EngineeringActas" a JOIN "OpeningRecords" o ON o."LegacyChecklistId"=a."ChecklistId" WHERE a."ProjectId"=p_project ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningDocumentScopes" ("OpeningRecordId","ProjectId","DocumentId","Kind","Version","Sha256","Declaration")
  SELECT l."OpeningRecordId",p_project,d."DocumentId",CASE WHEN d."Kind"=1 THEN 'REVISED' ELSE 'SIGNED' END,d."Version",d."Sha256",'LEGACY_ROUND_SCOPE'
  FROM "OpeningRecordDocuments" d JOIN "OpeningRoundLinks" l ON l."RoundId"=d."RoundId" WHERE d."ProjectId"=p_project ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningDocumentMembers" ("OpeningRecordId","ProjectId","DocumentId","StructureId")
  SELECT d."OpeningRecordId",p_project,d."DocumentId",m."StructureId" FROM "OpeningDocumentScopes" d
  JOIN "OpeningRecords" o ON o."Id"=d."OpeningRecordId" JOIN "OpeningRecordMembers" m ON m."OpeningRecordId"=o."Id"
  WHERE o."ProjectId"=p_project AND o."LegacyChecklistId" IS NOT NULL ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningAcceptances" ("Id","ProjectId","OpeningRecordId","RoundId","PresentedDocumentId","SignedDocumentId","At","ActorSubject","IsLegacy")
  SELECT r."Id",p_project,l."OpeningRecordId",r."Id",r."PresentedDocumentId",r."SignedDocumentId",r."RespondedAt",r."RespondedBySubject",true
  FROM "OpeningRecordRounds" r JOIN "OpeningRoundLinks" l ON l."RoundId"=r."Id" WHERE r."ProjectId"=p_project AND r."Status"=5 ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningAcceptanceMembers" ("AcceptanceId","ProjectId","OpeningRecordId","StructureId")
  SELECT a."Id",p_project,a."OpeningRecordId",r."StructureId" FROM "OpeningAcceptances" a
  JOIN "OpeningRecordStructures" r ON r."RoundId"=a."RoundId" AND r."Approval"=2
  JOIN "OpeningRecordMembers" m ON m."OpeningRecordId"=a."OpeningRecordId" AND m."StructureId"=r."StructureId"
  WHERE a."ProjectId"=p_project AND a."IsLegacy" ON CONFLICT DO NOTHING;
  INSERT INTO "OpeningFilingDeliveries" ("Id","ProjectId","AcceptanceId","TaskId")
  SELECT r."FilingTaskId",p_project,a."Id",r."FilingTaskId" FROM "OpeningAcceptances" a
  JOIN "OpeningRecordRounds" r ON r."Id"=a."RoundId" WHERE a."ProjectId"=p_project AND a."IsLegacy" ON CONFLICT DO NOTHING;
END;
$fn$;

-- No cross-project mutable lookup may turn incomplete legacy evidence into complete evidence.
CREATE FUNCTION sng_incremental_frozen() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN RAISE EXCEPTION 'Immutable incremental evidence' USING ERRCODE='23514'; END;
$fn$;
CREATE FUNCTION sng_incremental_complete_submission() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "MeasurementSubmissions" s WHERE s."Id"=NEW."SubmissionId" AND s."ProjectId"=NEW."ProjectId" AND s."EvidenceStatus"='COMPLETE') THEN
    RAISE EXCEPTION 'Complete frozen submission required' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_member_complete BEFORE INSERT ON "MeasurementSubmissionMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_complete_submission();
CREATE TRIGGER incremental_amendment_complete BEFORE INSERT ON "MeasurementAmendments" FOR EACH ROW EXECUTE FUNCTION sng_incremental_complete_submission();
CREATE TRIGGER incremental_receipt_complete BEFORE INSERT ON "EngineeringReceipts" FOR EACH ROW EXECUTE FUNCTION sng_incremental_complete_submission();

DO $backfill$
DECLARE p uuid;
BEGIN FOR p IN SELECT "Id" FROM "Projects" LOOP PERFORM sng_incremental_legacy_project(p); END LOOP; END;
$backfill$;

CREATE FUNCTION sng_incremental_snapshot_hash() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF TG_TABLE_NAME='MeasurementSubmissionMembers' THEN
    IF lower(NEW."SnapshotCanonical"::jsonb->>'Id') IS DISTINCT FROM NEW."StructureId"::text
       OR (NEW."EvidenceVersion">1 AND NOT EXISTS
         (SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=NEW."SubmissionId" AND m."StructureId"=NEW."StructureId" AND m."EvidenceVersion"=1)) THEN
      RAISE EXCEPTION 'Evidence must retain its original physical identity' USING ERRCODE='23514';
    END IF;
  END IF;
  IF upper(NEW."SnapshotSha256")<>upper(encode(sha256(convert_to(NEW."SnapshotCanonical",'UTF8')),'hex')) THEN
    RAISE EXCEPTION 'Snapshot hash mismatch' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_submission_hash BEFORE INSERT ON "MeasurementSubmissions" FOR EACH ROW EXECUTE FUNCTION sng_incremental_snapshot_hash();
CREATE TRIGGER incremental_member_hash BEFORE INSERT ON "MeasurementSubmissionMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_snapshot_hash();

CREATE FUNCTION sng_incremental_opening_member() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "OpeningRecords" o WHERE o."Id"=NEW."OpeningRecordId" AND o."ProjectId"=NEW."ProjectId" AND o."EvidenceStatus"='COMPLETE')
     OR NOT EXISTS (SELECT 1 FROM "EngineeringReceipts" e WHERE e."ProjectId"=NEW."ProjectId" AND e."SubmissionId"=NEW."SubmissionId") THEN
    RAISE EXCEPTION 'Complete received evidence required' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_opening_member BEFORE INSERT ON "OpeningRecordMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_opening_member();

CREATE FUNCTION sng_incremental_acceptance_member() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "OpeningAcceptances" a
    JOIN "OpeningDocumentMembers" s ON s."OpeningRecordId"=a."OpeningRecordId" AND s."DocumentId"=a."SignedDocumentId" AND s."StructureId"=NEW."StructureId"
    JOIN "OpeningDocumentMembers" p ON p."OpeningRecordId"=a."OpeningRecordId" AND p."DocumentId"=a."PresentedDocumentId" AND p."StructureId"=NEW."StructureId"
    WHERE a."Id"=NEW."AcceptanceId" AND a."ProjectId"=NEW."ProjectId") THEN
    RAISE EXCEPTION 'Signed and presented scopes must include the accepted structure' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_acceptance_member BEFORE INSERT ON "OpeningAcceptanceMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_acceptance_member();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "MeasurementSubmissions" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "MeasurementAmendments" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "MeasurementSubmissionMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "SubmissionReports" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "SubmissionDocumentScopes" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "SubmissionDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "EngineeringReceipts" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningRecords" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningRecordMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningRoundLinks" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningDocumentScopes" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningAcceptances" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningAcceptanceMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();
CREATE TRIGGER incremental_frozen BEFORE UPDATE OR DELETE ON "OpeningFilingDeliveries" FOR EACH ROW EXECUTE FUNCTION sng_incremental_frozen();

-- Native acceptances are explicit, nonempty and bound to the exact signed/presented scopes.
CREATE FUNCTION sng_incremental_acceptance() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NEW."IsLegacy" THEN
    IF NOT EXISTS (SELECT 1 FROM "OpeningRoundLinks" l JOIN "OpeningRecordRounds" r ON r."Id"=l."LegacyRoundId"
      WHERE l."RoundId"=NEW."RoundId" AND r."Status"=5 AND r."SignedDocumentId"=NEW."SignedDocumentId" AND r."PresentedDocumentId"=NEW."PresentedDocumentId") THEN
      RAISE EXCEPTION 'Legacy acceptance must reference its original accepted round' USING ERRCODE='23514';
    END IF;
  ELSE
    IF NOT EXISTS (SELECT 1 FROM "OpeningRecords" o WHERE o."Id"=NEW."OpeningRecordId" AND o."EvidenceStatus"='COMPLETE')
      OR NOT EXISTS (SELECT 1 FROM "OpeningAcceptanceMembers" m WHERE m."AcceptanceId"=NEW."Id")
      OR NOT EXISTS (SELECT 1 FROM "OpeningDocumentScopes" d WHERE d."OpeningRecordId"=NEW."OpeningRecordId" AND d."DocumentId"=NEW."SignedDocumentId" AND d."Kind"='SIGNED')
      OR NOT EXISTS (SELECT 1 FROM "OpeningDocumentScopes" d WHERE d."OpeningRecordId"=NEW."OpeningRecordId" AND d."DocumentId"=NEW."PresentedDocumentId" AND d."Kind" IN ('ENGINEERING','REVISED')) THEN
      RAISE EXCEPTION 'Explicit complete acceptance scope required' USING ERRCODE='23514';
    END IF;
  END IF;
  RETURN NULL;
END; $fn$;
CREATE CONSTRAINT TRIGGER incremental_acceptance AFTER INSERT ON "OpeningAcceptances" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_incremental_acceptance();

CREATE FUNCTION sng_incremental_filing() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "ProjectTasks" t JOIN "OpeningAcceptances" a ON a."Id"=NEW."AcceptanceId"
      WHERE t."Id"=NEW."TaskId" AND t."ProjectId"=NEW."ProjectId" AND t."Type"=10 AND
      ((NOT a."IsLegacy" AND t."IncrementalAcceptanceId"=a."Id") OR (a."IsLegacy" AND EXISTS
        (SELECT 1 FROM "OpeningRecordRounds" r WHERE r."Id"=a."RoundId" AND r."FilingTaskId"=t."Id")))) THEN
    RAISE EXCEPTION 'Filing task must belong to this acceptance' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_filing BEFORE INSERT ON "OpeningFilingDeliveries" FOR EACH ROW EXECUTE FUNCTION sng_incremental_filing();

CREATE FUNCTION sng_incremental_native_members() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF TG_TABLE_NAME='MeasurementSubmissions' THEN
    IF NOT NEW."IsLegacy" AND NOT EXISTS (SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=NEW."Id" AND m."EvidenceVersion"=1) THEN
      RAISE EXCEPTION 'Submission must contain frozen members' USING ERRCODE='23514';
    END IF;
  ELSE
    IF NOT EXISTS (SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=NEW."SubmissionId" AND m."AmendmentVersion"=NEW."Version") THEN
      RAISE EXCEPTION 'Amendment must contain affected evidence' USING ERRCODE='23514';
    END IF;
  END IF;
  RETURN NULL;
END; $fn$;
CREATE CONSTRAINT TRIGGER incremental_native_members AFTER INSERT ON "MeasurementSubmissions" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_incremental_native_members();
CREATE CONSTRAINT TRIGGER incremental_amendment_members AFTER INSERT ON "MeasurementAmendments" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sng_incremental_native_members();

-- Membership is frozen with its owning delivery/document/acceptance, not merely its row values.
-- A server-generated top-level transaction ID also works across EF savepoints; it is not business evidence.
CREATE FUNCTION sng_incremental_scope_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE permitted boolean := false; current_xid text := pg_current_xact_id()::text;
BEGIN
  CASE TG_TABLE_NAME
  WHEN 'MeasurementSubmissionMembers' THEN
    IF EXISTS (SELECT 1 FROM "MeasurementSubmissionMembers" m WHERE m."SubmissionId"=NEW."SubmissionId" AND m."StructureId"=NEW."StructureId" AND m."EvidenceVersion"=NEW."EvidenceVersion") THEN RETURN NEW; END IF;
    IF NEW."EvidenceVersion"=1 THEN
      SELECT s."CreationTransactionId"=current_xid INTO permitted FROM "MeasurementSubmissions" s WHERE s."Id"=NEW."SubmissionId";
    ELSE
      SELECT a."CreationTransactionId"=current_xid INTO permitted FROM "MeasurementAmendments" a WHERE a."SubmissionId"=NEW."SubmissionId" AND a."Version"=NEW."AmendmentVersion";
    END IF;
  WHEN 'SubmissionDocumentScopes' THEN
    IF EXISTS (SELECT 1 FROM "SubmissionDocumentScopes" d WHERE d."SubmissionId"=NEW."SubmissionId" AND d."DocumentId"=NEW."DocumentId") THEN RETURN NEW; END IF;
    SELECT s."CreationTransactionId"=current_xid OR EXISTS (SELECT 1 FROM "MeasurementAmendments" a WHERE a."SubmissionId"=s."Id" AND a."CreationTransactionId"=current_xid)
      INTO permitted FROM "MeasurementSubmissions" s WHERE s."Id"=NEW."SubmissionId";
  WHEN 'SubmissionDocumentMembers' THEN
    IF EXISTS (SELECT 1 FROM "SubmissionDocumentMembers" m WHERE m."SubmissionId"=NEW."SubmissionId" AND m."DocumentId"=NEW."DocumentId" AND m."StructureId"=NEW."StructureId" AND m."EvidenceVersion"=NEW."EvidenceVersion") THEN RETURN NEW; END IF;
    SELECT d."CreationTransactionId"=current_xid INTO permitted FROM "SubmissionDocumentScopes" d WHERE d."SubmissionId"=NEW."SubmissionId" AND d."DocumentId"=NEW."DocumentId";
  WHEN 'SubmissionReports' THEN
    IF EXISTS (SELECT 1 FROM "SubmissionReports" r WHERE r."SubmissionId"=NEW."SubmissionId" AND r."Version"=NEW."Version") THEN RETURN NEW; END IF;
    SELECT s."CreationTransactionId"=current_xid OR EXISTS (SELECT 1 FROM "MeasurementAmendments" a WHERE a."SubmissionId"=s."Id" AND a."Version"=NEW."Version" AND a."CreationTransactionId"=current_xid)
      INTO permitted FROM "MeasurementSubmissions" s WHERE s."Id"=NEW."SubmissionId";
  WHEN 'OpeningAcceptanceMembers' THEN
    IF EXISTS (SELECT 1 FROM "OpeningAcceptanceMembers" m WHERE m."AcceptanceId"=NEW."AcceptanceId" AND m."StructureId"=NEW."StructureId") THEN RETURN NEW; END IF;
    SELECT a."CreationTransactionId"=current_xid INTO permitted FROM "OpeningAcceptances" a WHERE a."Id"=NEW."AcceptanceId";
  WHEN 'OpeningDocumentMembers' THEN
    IF EXISTS (SELECT 1 FROM "OpeningDocumentMembers" m WHERE m."OpeningRecordId"=NEW."OpeningRecordId" AND m."DocumentId"=NEW."DocumentId" AND m."StructureId"=NEW."StructureId") THEN RETURN NEW; END IF;
    SELECT d."CreationTransactionId"=current_xid INTO permitted FROM "OpeningDocumentScopes" d WHERE d."OpeningRecordId"=NEW."OpeningRecordId" AND d."DocumentId"=NEW."DocumentId";
  END CASE;
  IF permitted IS DISTINCT FROM true THEN RAISE EXCEPTION 'Frozen scope cannot gain members or documents later' USING ERRCODE='23514'; END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "MeasurementSubmissionMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "SubmissionDocumentScopes" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "SubmissionDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "SubmissionReports" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "OpeningAcceptanceMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();
CREATE TRIGGER incremental_scope_insert BEFORE INSERT ON "OpeningDocumentMembers" FOR EACH ROW EXECUTE FUNCTION sng_incremental_scope_insert();

CREATE FUNCTION sng_incremental_report_integrity() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NEW."LegacyChecklistId" IS NULL AND (
    upper(NEW."PdfSha256")<>upper(encode(sha256(NEW."Pdf"),'hex')) OR
    upper(NEW."SnapshotSha256")<>upper(encode(sha256(convert_to(NEW."SnapshotCanonical",'UTF8')),'hex'))) THEN
    RAISE EXCEPTION 'Native report integrity mismatch' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END; $fn$;
CREATE TRIGGER incremental_report_integrity BEFORE INSERT ON "SubmissionReports" FOR EACH ROW EXECUTE FUNCTION sng_incremental_report_integrity();

CREATE FUNCTION sng_incremental_task_binding() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "OpeningAcceptances" a JOIN "OpeningFilingDeliveries" f ON f."AcceptanceId"=a."Id"
      WHERE a."Id"=NEW."IncrementalAcceptanceId" AND NOT a."IsLegacy" AND f."TaskId"=NEW."Id" AND a."ProjectId"=NEW."ProjectId") THEN
    RAISE EXCEPTION 'Incremental filing task requires its explicit acceptance and delivery' USING ERRCODE='23514';
  END IF;
  RETURN NULL;
END; $fn$;
CREATE CONSTRAINT TRIGGER incremental_task_binding AFTER INSERT OR UPDATE ON "ProjectTasks"
DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."IncrementalAcceptanceId" IS NOT NULL) EXECUTE FUNCTION sng_incremental_task_binding();
