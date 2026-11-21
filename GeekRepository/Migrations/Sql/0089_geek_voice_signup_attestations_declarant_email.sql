-- Geek-Receptionist signup, Jeff 2026-11-21: the first screen asks for two emails, the person's own and the business's. The business's is
-- the tenant's contact email (tenants.contact_email_encrypted). The person's own belongs with the declaration they made, so it is stored
-- here, sealed by GeekAPI (VoiceSecretPurposes SignupDeclarantEmail) and kept forever by the trigger from 0087.
--
-- Added as its own migration, not edited into 0088, which is already deployed. A row written before it (none are expected) keeps an
-- empty value; the NOT VALID check holds every row written after. The CHECK names the column first, which the migration guard needs.

ALTER TABLE geek_voice.signup_attestations
    ADD COLUMN IF NOT EXISTS declarant_email_encrypted TEXT NOT NULL DEFAULT '';

ALTER TABLE geek_voice.signup_attestations
    ALTER COLUMN declarant_email_encrypted DROP DEFAULT;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_signup_attestations_declarant_email') THEN
        ALTER TABLE geek_voice.signup_attestations
            ADD CONSTRAINT ck_voice_signup_attestations_declarant_email CHECK (declarant_email_encrypted !~ '^\s*$') NOT VALID;
    END IF;
END
$$;
