using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Claims.Api.Data.Migrations;

public partial class AddApprovedStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Offline migration: EF runs this entire SQL operation and migration history in one transaction.
        // Keep foreign keys ON. Back up and drop dependent tables before replacing their parent,
        // then recreate them before copying the children back; this avoids ON DELETE CASCADE.
        migrationBuilder.Sql("""
            -- Refuse schemas with custom triggers or indexes that this rebuild cannot preserve.
            CREATE TEMP TABLE __claims_schema_guard (unexpected INTEGER NOT NULL CHECK (unexpected = 0));
            INSERT INTO __claims_schema_guard SELECT CASE WHEN (SELECT foreign_keys FROM pragma_foreign_keys) = 1 THEN 0 ELSE 1 END;
            INSERT INTO __claims_schema_guard
                SELECT count(*) FROM sqlite_schema AS t, pragma_foreign_key_list(t.name) AS fk
                WHERE t.type = 'table' AND fk."table" IN ('Claims', 'ClaimHistory', 'InformationRequests')
                  AND t.name NOT IN ('ClaimHistory', 'InformationRequests');
            INSERT INTO __claims_schema_guard SELECT count(*) FROM sqlite_schema
                WHERE tbl_name IN ('Claims', 'ClaimHistory', 'InformationRequests')
                  AND (type = 'trigger' OR (type = 'index' AND name NOT IN (
                    'IX_Claims_AssignedOfficerId', 'IX_Claims_ClaimantId', 'IX_Claims_ClaimNumber',
                    'IX_ClaimHistory_ActorId', 'IX_ClaimHistory_ClaimId',
                    'IX_InformationRequests_ClaimId', 'IX_InformationRequests_RequestedByOfficerId')));
            -- The rebuild targets the committed schema only, including column order and foreign keys.
            INSERT INTO __claims_schema_guard SELECT CASE WHEN
                (SELECT group_concat(name, ',') FROM pragma_table_info('Claims')) = 'Id,ClaimNumber,ClaimantId,AssignedOfficerId,ClaimType,IncidentDescription,IncidentDate,MarketCode,CurrencyCode,ClaimedAmountSen,EstimatedLiabilitySen,Status,CreatedAt,UpdatedAt'
                AND (SELECT group_concat(name, ',') FROM pragma_table_info('ClaimHistory')) = 'Id,ClaimId,ActorId,Action,Notes,CreatedAt'
                AND (SELECT group_concat(name, ',') FROM pragma_table_info('InformationRequests')) = 'Id,ClaimId,RequestedByOfficerId,Message,Response,RequestedAt,RespondedAt'
                AND (SELECT count(*) FROM pragma_foreign_key_list('Claims')) = 2
                AND (SELECT count(*) FROM pragma_foreign_key_list('ClaimHistory')) = 2
                AND (SELECT count(*) FROM pragma_foreign_key_list('InformationRequests')) = 2
                AND (SELECT (length(upper(sql)) - length(replace(upper(sql), 'CHECK', ''))) / 5 FROM sqlite_schema WHERE type='table' AND name='Claims') = 6
                AND (SELECT (length(upper(sql)) - length(replace(upper(sql), 'CHECK', ''))) / 5 FROM sqlite_schema WHERE type='table' AND name='ClaimHistory') = 2
                AND (SELECT (length(upper(sql)) - length(replace(upper(sql), 'CHECK', ''))) / 5 FROM sqlite_schema WHERE type='table' AND name='InformationRequests') = 2
                THEN 0 ELSE 1 END;
            DROP TABLE __claims_schema_guard;
            CREATE TEMP TABLE __claims_history_backup AS SELECT * FROM "ClaimHistory";
            CREATE TEMP TABLE __claims_requests_backup AS SELECT * FROM "InformationRequests";
            CREATE TEMP TABLE __claims_sequence_backup AS
                SELECT name, seq FROM sqlite_sequence WHERE name IN ('Claims', 'ClaimHistory', 'InformationRequests');

            DROP TABLE "ClaimHistory";
            DROP TABLE "InformationRequests";

            CREATE TABLE "__new_Claims" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Claims" PRIMARY KEY AUTOINCREMENT,
                "ClaimNumber" TEXT NOT NULL,
                "ClaimantId" INTEGER NOT NULL,
                "AssignedOfficerId" INTEGER NULL,
                "ClaimType" TEXT NOT NULL,
                "IncidentDescription" TEXT NOT NULL,
                "IncidentDate" TEXT NOT NULL,
                "MarketCode" TEXT NOT NULL,
                "CurrencyCode" TEXT NOT NULL,
                "ClaimedAmountSen" INTEGER NOT NULL,
                "EstimatedLiabilitySen" INTEGER NOT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "CK_Claims_Amounts" CHECK (typeof("ClaimedAmountSen") = 'integer' AND "ClaimedAmountSen" > 0 AND typeof("EstimatedLiabilitySen") = 'integer' AND "EstimatedLiabilitySen" >= 0),
                CONSTRAINT "CK_Claims_ClaimNumber" CHECK (length(trim("ClaimNumber")) BETWEEN 1 AND 40),
                CONSTRAINT "CK_Claims_Description" CHECK (length(trim("IncidentDescription")) BETWEEN 1 AND 2000),
                CONSTRAINT "CK_Claims_MarketCurrency" CHECK ("MarketCode" = 'MY' AND "CurrencyCode" = 'MYR'),
                CONSTRAINT "CK_Claims_Status" CHECK ("Status" IN ('Submitted', 'UnderReview', 'InfoRequested', 'Settled', 'Rejected', 'Approved')),
                CONSTRAINT "CK_Claims_Type" CHECK ("ClaimType" IN ('Motor', 'Property')),
                CONSTRAINT "FK_Claims_Users_AssignedOfficerId" FOREIGN KEY ("AssignedOfficerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Claims_Users_ClaimantId" FOREIGN KEY ("ClaimantId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            INSERT INTO "__new_Claims" ("Id", "ClaimNumber", "ClaimantId", "AssignedOfficerId", "ClaimType", "IncidentDescription", "IncidentDate", "MarketCode", "CurrencyCode", "ClaimedAmountSen", "EstimatedLiabilitySen", "Status", "CreatedAt", "UpdatedAt")
                SELECT "Id", "ClaimNumber", "ClaimantId", "AssignedOfficerId", "ClaimType", "IncidentDescription", "IncidentDate", "MarketCode", "CurrencyCode", "ClaimedAmountSen", "EstimatedLiabilitySen", "Status", "CreatedAt", "UpdatedAt" FROM "Claims";
            DROP TABLE "Claims";
            ALTER TABLE "__new_Claims" RENAME TO "Claims";

            CREATE TABLE "ClaimHistory" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ClaimHistory" PRIMARY KEY AUTOINCREMENT,
                "ClaimId" INTEGER NOT NULL, "ActorId" INTEGER NOT NULL, "Action" TEXT NOT NULL,
                "Notes" TEXT NULL, "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "CK_History_Action" CHECK (length(trim("Action")) BETWEEN 1 AND 100),
                CONSTRAINT "CK_History_Notes" CHECK ("Notes" IS NULL OR length("Notes") <= 2000),
                CONSTRAINT "FK_ClaimHistory_Claims_ClaimId" FOREIGN KEY ("ClaimId") REFERENCES "Claims" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ClaimHistory_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            INSERT INTO "ClaimHistory" SELECT * FROM __claims_history_backup;
            CREATE TABLE "InformationRequests" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InformationRequests" PRIMARY KEY AUTOINCREMENT,
                "ClaimId" INTEGER NOT NULL, "RequestedByOfficerId" INTEGER NOT NULL, "Message" TEXT NOT NULL,
                "Response" TEXT NULL, "RequestedAt" TEXT NOT NULL, "RespondedAt" TEXT NULL,
                CONSTRAINT "CK_Requests_Message" CHECK (length(trim("Message")) BETWEEN 1 AND 2000),
                CONSTRAINT "CK_Requests_Response" CHECK ("Response" IS NULL OR length("Response") <= 2000),
                CONSTRAINT "FK_InformationRequests_Claims_ClaimId" FOREIGN KEY ("ClaimId") REFERENCES "Claims" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_InformationRequests_Users_RequestedByOfficerId" FOREIGN KEY ("RequestedByOfficerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            INSERT INTO "InformationRequests" SELECT * FROM __claims_requests_backup;

            CREATE INDEX "IX_Claims_AssignedOfficerId" ON "Claims" ("AssignedOfficerId");
            CREATE INDEX "IX_Claims_ClaimantId" ON "Claims" ("ClaimantId");
            CREATE UNIQUE INDEX "IX_Claims_ClaimNumber" ON "Claims" ("ClaimNumber");
            CREATE INDEX "IX_ClaimHistory_ActorId" ON "ClaimHistory" ("ActorId");
            CREATE INDEX "IX_ClaimHistory_ClaimId" ON "ClaimHistory" ("ClaimId");
            CREATE INDEX "IX_InformationRequests_ClaimId" ON "InformationRequests" ("ClaimId");
            CREATE INDEX "IX_InformationRequests_RequestedByOfficerId" ON "InformationRequests" ("RequestedByOfficerId");

            -- Restore AUTOINCREMENT high-water marks even when the highest old row was deleted.
            DELETE FROM sqlite_sequence WHERE name IN (SELECT name FROM __claims_sequence_backup);
            INSERT INTO sqlite_sequence(name, seq) SELECT name, seq FROM __claims_sequence_backup;
            DROP TABLE __claims_history_backup;
            DROP TABLE __claims_requests_backup;
            DROP TABLE __claims_sequence_backup;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Downgrading cannot safely represent Approved claims. Restore a backup instead.");
}
