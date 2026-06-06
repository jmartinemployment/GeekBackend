-- Idempotent: niche analyzer step log (Phase 1.5)

ALTER TABLE geek_seo.niche_profiles
    ADD COLUMN IF NOT EXISTS "AnalysisStepLog" jsonb NOT NULL DEFAULT '[]'::jsonb,
    ADD COLUMN IF NOT EXISTS "AnalysisStepLogVersion" integer NOT NULL DEFAULT 1;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260606120000_AddNicheProfileAnalysisStepLog', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;
