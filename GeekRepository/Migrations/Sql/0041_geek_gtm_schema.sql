-- 0041: give the GTM account store a schema GeekRepository owns.
--
-- Google Tag Manager is a separate product (Geek-GTM-MCP, an MCP stdio server for GTM
-- containers). Its Google OAuth refresh-token store was never a Geek-SEO feature -- it was
-- built on Geek-SEO's plumbing, reusing SeoDbContext, the geek_seo schema and
-- SeoCredentialProtector. When the unauthorized Geek-SEO layer was deleted on 2026-09-29 the
-- GTM controller stopped compiling, because it referenced all three.
--
-- GeekRepository now owns this table outright: schema geek_gtm, entity GtmAccountConnection,
-- context GtmDbContext, encryption via GtmCredentialProtector -- a copy of the Geek-SEO helper,
-- per AGENTS.md "copy, never reuse". Nothing here references Geek-SEO.
--
-- Raw SQL rather than an EF migration, matching geek_blog: this script both creates the schema
-- and relocates existing rows, which an EF migration cannot do in one step. GtmDbContext
-- therefore has no EF migrations and no MigrationsHistoryTable.
--
-- Column names are quoted PascalCase because that is how the rows exist today -- EF created
-- seo_gtm_account_connections with default conventions and no naming policy. Renaming columns
-- and moving the data in the same script would leave no way to verify the copy, so the shape is
-- preserved exactly.

CREATE SCHEMA IF NOT EXISTS geek_gtm;

CREATE TABLE IF NOT EXISTS geek_gtm.gtm_account_connections (
    "Id"                    uuid PRIMARY KEY,
    "UserId"                uuid NOT NULL,
    "AccountKey"            character varying(128) NOT NULL,
    "GoogleEmail"           text NULL,
    "EncryptedRefreshToken" bytea NOT NULL,
    "EncryptionIv"          bytea NOT NULL,
    "EncryptionTag"         bytea NOT NULL,
    "ConnectedAt"           timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_gtm_account_connections_user_account
    ON geek_gtm.gtm_account_connections ("UserId", "AccountKey");

-- Relocate live credentials, if the old table is still present. to_regclass returns NULL rather
-- than raising when the schema or table is absent, so this is a no-op on a database built from
-- the current scripts (geek_seo is never created there -- nothing creates that schema any more).
--
-- ON CONFLICT DO NOTHING: re-running must not clobber a token that has since been re-connected
-- through the new table. The old rows are the fallback, not the winner.
DO $$
BEGIN
    IF to_regclass('geek_seo.seo_gtm_account_connections') IS NOT NULL THEN
        INSERT INTO geek_gtm.gtm_account_connections (
            "Id", "UserId", "AccountKey", "GoogleEmail",
            "EncryptedRefreshToken", "EncryptionIv", "EncryptionTag", "ConnectedAt")
        SELECT
            "Id", "UserId", "AccountKey", "GoogleEmail",
            "EncryptedRefreshToken", "EncryptionIv", "EncryptionTag", "ConnectedAt"
        FROM geek_seo.seo_gtm_account_connections
        ON CONFLICT ("Id") DO NOTHING;

        RAISE NOTICE 'geek_gtm: copied % row(s) from geek_seo.seo_gtm_account_connections',
            (SELECT count(*) FROM geek_seo.seo_gtm_account_connections);
    END IF;
END $$;

-- The source table is deliberately NOT dropped here. These are live Google refresh tokens for a
-- product served by a different repo; the copy is verified in production before the original
-- goes. Dropping it belongs in its own script, once Geek-GTM-MCP is confirmed reading from
-- geek_gtm.
