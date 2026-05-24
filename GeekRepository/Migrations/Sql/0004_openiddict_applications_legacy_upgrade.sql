-- Legacy deployments created openiddict_applications with a reduced column set
-- (id, client_id, client_secret, display_name, redirect_uris, permissions, type).
-- 0001 uses CREATE TABLE IF NOT EXISTS, so those databases never received the
-- OpenIddict 7 columns. Add missing columns idempotently.

ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS application_type TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS client_type TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS consent_type TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS display_names TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS json_web_key_set TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS post_logout_redirect_uris TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS properties TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS requirements TEXT;
ALTER TABLE openiddict_applications ADD COLUMN IF NOT EXISTS settings TEXT;

-- Backfill client_type from legacy "type" column when present.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'openiddict_applications'
          AND column_name = 'type'
    ) THEN
        EXECUTE $migration$
            UPDATE openiddict_applications
            SET client_type = type
            WHERE client_type IS NULL AND type IS NOT NULL
        $migration$;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ix_openiddict_applications_client_id
    ON openiddict_applications (client_id)
    WHERE client_id IS NOT NULL;
