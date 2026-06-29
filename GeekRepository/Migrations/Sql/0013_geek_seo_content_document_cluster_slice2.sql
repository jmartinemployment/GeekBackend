-- Slice 2: publish slug + spoke provenance on content documents.

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "PublishSlug" character varying(200);

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "SpokeSourceType" character varying(32);

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "SpokeSourcePhrase" text;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_seo_content_documents_ProjectId_PublishSlug"
    ON geek_seo.seo_content_documents ("ProjectId", "PublishSlug")
    WHERE "PublishSlug" IS NOT NULL;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260629190134_AddContentDocumentClusterSlice2', '10.0.9')
ON CONFLICT ("MigrationId") DO NOTHING;
