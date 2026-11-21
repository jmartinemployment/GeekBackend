-- Geek-Receptionist signup, decided by Jeff 2026-11-21 (plans/attestation-and-terms.md, stages 2 and 3): a named person declares
-- that they may answer for the business and that the answers are true, and accepts the Subscription Terms, before a tenant exists.
-- One row per tenant, written in the same transaction as the tenant, kept forever.
--
-- The row exists only for a person who agreed: a No to the declaration or to the Terms stops the signup and creates no tenant, so
-- there is no "agreed" column to be false. That is enforced by the signup gate (GeekAPI refuses the signup) and by the foreign key
-- below, which makes a row impossible without its tenant. What was agreed to is the exact words shown, stored here beside the
-- person, because a version string for a Terms document that does not exist yet would be a claim with nothing behind it.
--
-- The declarant's name and title and the business's address are sealed by GeekAPI before they get here (VoiceSecretPurposes
-- SignupAttestorName / SignupAttestorTitle / SignupBusinessAddress), as the contact email and phone are: this table holds ciphertext
-- and GeekRepository never reads it.
--
-- "Forever" is enforced here, as it is for signup_industry_answers (0068): a trigger refuses every UPDATE, DELETE and TRUNCATE, and a
-- tenant that has an attestation cannot be deleted (ON DELETE RESTRICT). No code deletes from it.
--
-- The table has no row-level-security policy, like signup_industry_answers: nothing serves its rows to a tenant.

CREATE TABLE IF NOT EXISTS geek_voice.signup_attestations (
    id                      UUID        PRIMARY KEY,
    tenant_id               UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE RESTRICT,
    attestor_name_encrypted TEXT        NOT NULL,
    attestor_title_encrypted TEXT       NOT NULL,
    business_address_encrypted TEXT     NOT NULL,
    declaration_statement   TEXT        NOT NULL,
    terms_statement         TEXT        NOT NULL,
    attested_at             TIMESTAMPTZ NOT NULL,
    ip_address              INET        NOT NULL,
    visitor_country         TEXT        NOT NULL,
    CONSTRAINT uq_voice_signup_attestations_tenant UNIQUE (tenant_id),
    CONSTRAINT ck_voice_signup_attestations_name CHECK (btrim(attestor_name_encrypted) <> ''),
    CONSTRAINT ck_voice_signup_attestations_title CHECK (btrim(attestor_title_encrypted) <> ''),
    CONSTRAINT ck_voice_signup_attestations_address CHECK (btrim(business_address_encrypted) <> ''),
    CONSTRAINT ck_voice_signup_attestations_declaration CHECK (btrim(declaration_statement) <> ''),
    CONSTRAINT ck_voice_signup_attestations_terms CHECK (btrim(terms_statement) <> ''),
    CONSTRAINT ck_voice_signup_attestations_country CHECK (btrim(visitor_country) <> '')
);

CREATE INDEX IF NOT EXISTS ix_voice_signup_attestations_attested_at ON geek_voice.signup_attestations (attested_at);

CREATE OR REPLACE FUNCTION geek_voice.refuse_signup_attestation_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'geek_voice.signup_attestations is kept forever: % is refused', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS trg_voice_signup_attestations_keep_rows ON geek_voice.signup_attestations;
CREATE TRIGGER trg_voice_signup_attestations_keep_rows
    BEFORE UPDATE OR DELETE ON geek_voice.signup_attestations
    FOR EACH ROW EXECUTE FUNCTION geek_voice.refuse_signup_attestation_change();

DROP TRIGGER IF EXISTS trg_voice_signup_attestations_keep_table ON geek_voice.signup_attestations;
CREATE TRIGGER trg_voice_signup_attestations_keep_table
    BEFORE TRUNCATE ON geek_voice.signup_attestations
    FOR EACH STATEMENT EXECUTE FUNCTION geek_voice.refuse_signup_attestation_change();
