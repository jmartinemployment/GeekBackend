-- Slice 3: pillar link plan JSON on content documents.

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "LinkPlanJson" text;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260629194211_AddContentDocumentClusterSlice3', '10.0.9')
ON CONFLICT ("MigrationId") DO NOTHING;
