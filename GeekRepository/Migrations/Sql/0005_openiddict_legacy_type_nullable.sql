-- Legacy openiddict_applications required NOT NULL "type" (public/confidential).
-- OpenIddict 7 writes client_type; without this, inserts fail with SQLSTATE 23502.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'openiddict_applications'
          AND column_name = 'type'
    ) THEN
        ALTER TABLE openiddict_applications ALTER COLUMN type DROP NOT NULL;

        EXECUTE $migration$
            UPDATE openiddict_applications
            SET type = client_type
            WHERE type IS NULL AND client_type IS NOT NULL
        $migration$;
    END IF;
END $$;
