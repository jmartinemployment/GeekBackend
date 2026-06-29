-- Slice 1: parent/child cluster columns on content documents.

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "ParentDocumentId" uuid;

ALTER TABLE geek_seo.seo_content_documents
    ADD COLUMN IF NOT EXISTS "DocumentKind" character varying(32) NOT NULL DEFAULT 'standalone';

CREATE INDEX IF NOT EXISTS "IX_seo_content_documents_ParentDocumentId"
    ON geek_seo.seo_content_documents ("ParentDocumentId");

CREATE INDEX IF NOT EXISTS "IX_seo_content_documents_ProjectId_DocumentKind"
    ON geek_seo.seo_content_documents ("ProjectId", "DocumentKind");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_seo_content_documents_seo_content_documents_ParentDocumentId'
    ) THEN
        ALTER TABLE geek_seo.seo_content_documents
            ADD CONSTRAINT "FK_seo_content_documents_seo_content_documents_ParentDocumentId"
            FOREIGN KEY ("ParentDocumentId")
            REFERENCES geek_seo.seo_content_documents ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260629185456_AddContentDocumentClusterSlice1', '10.0.9')
ON CONFLICT ("MigrationId") DO NOTHING;
