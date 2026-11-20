-- Geek-Receptionist, the object-storage half of bring-your-own-storage (plans/client-and-app-plan.md, "AWS S3 / Google Cloud Storage
-- (Standard & Above)"). Google Drive is an OAuth connection and lives in tenant_mcp_connections; a bucket is a pair of long-lived keys
-- and a place to put things, which is a different shape, so it gets its own table rather than being bent into the OAuth one.
--
-- The keys are ciphertext GeekAPI produced; this database never sees them in the clear and has no way to decrypt them. Row-level
-- security is forced and the policy is the same tenant isolation every other table in this schema has. Idempotent.

CREATE TABLE IF NOT EXISTS geek_voice.storage_connections (
    tenant_id                   UUID PRIMARY KEY REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    provider                    TEXT        NOT NULL
                                CHECK (provider IN ('aws_s3', 'google_cloud_storage')),
    access_key_id_encrypted     TEXT        NOT NULL,
    secret_access_key_encrypted TEXT        NOT NULL,
    bucket                      TEXT        NOT NULL,
    key_prefix                  TEXT        NOT NULL DEFAULT '',
    region                      TEXT        NOT NULL,
    is_active                   BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE geek_voice.storage_connections ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.storage_connections FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.storage_connections;
CREATE POLICY tenant_isolation ON geek_voice.storage_connections
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
