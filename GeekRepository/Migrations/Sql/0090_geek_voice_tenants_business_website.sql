-- Geek-Receptionist signup, Jeff 2026-11-21: the first screen asks for the business's website. It is the business's own public address, not
-- a personal detail, so it is stored plain on the tenant and not sealed. It is optional: a business with no website leaves it empty, and
-- NULL says so. When there is one it is an absolute http or https address, which GeekAPI checks and this constraint enforces.

ALTER TABLE geek_voice.tenants
    ADD COLUMN IF NOT EXISTS business_website TEXT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_tenants_business_website') THEN
        ALTER TABLE geek_voice.tenants
            ADD CONSTRAINT ck_voice_tenants_business_website CHECK (business_website IS NULL OR business_website ~ '^https?://[^\s/]+\.[^\s/]+') NOT VALID;
    END IF;
END
$$;
