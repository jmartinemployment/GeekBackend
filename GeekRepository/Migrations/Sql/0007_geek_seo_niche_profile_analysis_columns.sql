-- Idempotent: niche analyzer progress columns (EF migrations 20260602193000 / 20260602194500
-- were hand-authored without Designer files, so MigrateAsync never applied them).

ALTER TABLE geek_seo.niche_profiles
    ADD COLUMN IF NOT EXISTS "AnalysisStep" text,
    ADD COLUMN IF NOT EXISTS "AnalysisStepNumber" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "AnalysisTotalSteps" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "AnalysisProgressAt" timestamptz;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260602193000_AddNicheProfileAnalysisStep', '10.0.7'),
    ('20260602194500_AddNicheProfileProgressAt', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;
