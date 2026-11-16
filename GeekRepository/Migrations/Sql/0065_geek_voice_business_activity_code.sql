-- Geek-Receptionist signup (docs/Geek-Receptionist.md section 5, item 1), decided 2026-11-16: a business says what it does with one of
-- the IRS Principal Business Activity codes of the 2025 Schedule C chart, and GeekAPI decides the healthcare and addendum rules on the
-- code. industry_vertical keeps the code's name for the receptionist's script. A tenant written before this has no code, so the column
-- is nullable; GeekRepository refuses a new tenant without six digits here, and the check holds the shape of every row written.

ALTER TABLE geek_voice.tenants ADD COLUMN IF NOT EXISTS business_activity_code TEXT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_tenants_business_activity_code') THEN
        ALTER TABLE geek_voice.tenants ADD CONSTRAINT ck_voice_tenants_business_activity_code
            CHECK (business_activity_code IS NULL OR business_activity_code ~ '^[0-9]{6}$') NOT VALID;
    END IF;
END
$$;
