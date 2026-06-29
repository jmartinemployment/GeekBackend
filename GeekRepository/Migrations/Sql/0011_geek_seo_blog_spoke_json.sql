-- Idempotent: blog spoke JSON on content documents.

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "BlogSpokeJson" text;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260629171247_AddBlogSpokeToContentDocuments', '10.0.9')
ON CONFLICT ("MigrationId") DO NOTHING;
