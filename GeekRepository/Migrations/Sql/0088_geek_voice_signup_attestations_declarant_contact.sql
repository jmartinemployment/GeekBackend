-- Geek-Receptionist signup, Jeff 2026-11-21: the first screen also asks the person's own address and telephone (First Name, Middle
-- Initial, Last Name, Address, City, State, Zip Code, Telephone), beside the business's. They are part of who made the declaration, so
-- they are stored with it, sealed by GeekAPI like the name and the business address (VoiceSecretPurposes SignupDeclarantAddress /
-- SignupDeclarantPhone), and kept forever by the same trigger as the rest of signup_attestations (0087).
--
-- 0087 was applied before these columns existed, so they are added here and not edited into it. A row written before this migration
-- (none are expected: the table was created the same day) keeps an empty value; the NOT VALID checks hold every row written after.
-- ALTER TABLE is not an UPDATE, DELETE or TRUNCATE, so the forever-trigger does not refuse it.

ALTER TABLE geek_voice.signup_attestations
    ADD COLUMN IF NOT EXISTS declarant_address_encrypted TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS declarant_phone_encrypted   TEXT NOT NULL DEFAULT '';

ALTER TABLE geek_voice.signup_attestations
    ALTER COLUMN declarant_address_encrypted DROP DEFAULT,
    ALTER COLUMN declarant_phone_encrypted   DROP DEFAULT;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_signup_attestations_declarant_address') THEN
        ALTER TABLE geek_voice.signup_attestations
            ADD CONSTRAINT ck_voice_signup_attestations_declarant_address CHECK (declarant_address_encrypted !~ '^\s*$') NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_signup_attestations_declarant_phone') THEN
        ALTER TABLE geek_voice.signup_attestations
            ADD CONSTRAINT ck_voice_signup_attestations_declarant_phone CHECK (declarant_phone_encrypted !~ '^\s*$') NOT VALID;
    END IF;
END
$$;
