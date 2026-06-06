ALTER TABLE geek_seo.niche_profiles
    ADD COLUMN IF NOT EXISTS "FusionSnapshot" jsonb NULL;

INSERT INTO geek_seo."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260606200000_AddNicheProfileFusionSnapshot', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;
