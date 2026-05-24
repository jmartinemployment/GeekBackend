-- Normalize legacy openiddict_applications to the OpenIddict 7 shape in 0001:
-- TEXT primary keys (32-char hex, no dashes) and client_type only (drop legacy "type").

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
            SET client_type = COALESCE(client_type, type)
            WHERE client_type IS NULL AND type IS NOT NULL
        $migration$;

        ALTER TABLE openiddict_applications DROP COLUMN type;
    END IF;
END $$;

DO $$
DECLARE
    id_udt text;
BEGIN
    SELECT udt_name
    INTO id_udt
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'openiddict_applications'
      AND column_name = 'id';

    IF id_udt = 'uuid' THEN
        ALTER TABLE openiddict_authorizations
            DROP CONSTRAINT IF EXISTS openiddict_authorizations_application_id_fkey;
        ALTER TABLE openiddict_tokens
            DROP CONSTRAINT IF EXISTS openiddict_tokens_application_id_fkey;

        ALTER TABLE openiddict_applications
            ALTER COLUMN id DROP DEFAULT;

        ALTER TABLE openiddict_applications
            ALTER COLUMN id TYPE TEXT
            USING replace(id::text, '-', '');

        IF EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'openiddict_authorizations'
              AND column_name = 'application_id'
              AND udt_name = 'uuid'
        ) THEN
            ALTER TABLE openiddict_authorizations
                ALTER COLUMN application_id TYPE TEXT
                USING CASE
                    WHEN application_id IS NULL THEN NULL
                    ELSE replace(application_id::text, '-', '')
                END;
        END IF;

        IF EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'openiddict_tokens'
              AND column_name = 'application_id'
              AND udt_name = 'uuid'
        ) THEN
            ALTER TABLE openiddict_tokens
                ALTER COLUMN application_id TYPE TEXT
                USING CASE
                    WHEN application_id IS NULL THEN NULL
                    ELSE replace(application_id::text, '-', '')
                END;
        END IF;

        ALTER TABLE openiddict_authorizations
            ADD CONSTRAINT openiddict_authorizations_application_id_fkey
            FOREIGN KEY (application_id) REFERENCES openiddict_applications (id) ON DELETE CASCADE;

        ALTER TABLE openiddict_tokens
            ADD CONSTRAINT openiddict_tokens_application_id_fkey
            FOREIGN KEY (application_id) REFERENCES openiddict_applications (id) ON DELETE CASCADE;
    END IF;
END $$;

-- Incomplete rows from failed deploys block OpenIddictClientSeeder (it skips when client_id exists).
DELETE FROM openiddict_applications
WHERE client_id = 'geek-seo-electron'
  AND (requirements IS NULL OR requirements = '[]' OR permissions = '[]');
