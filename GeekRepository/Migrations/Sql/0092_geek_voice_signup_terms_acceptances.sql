-- Geek-Receptionist signup, decided by Jeff 2026-11-22: the Terms are accepted after the receptionist's setup and before the plan and the
-- payment, not when the business is created. So the business (and the sealed details of the person who signed it up) is written first,
-- and the acceptance is written later, by its own call, as its own row.
--
-- signup_attestations.terms_statement stops being required: a row now exists from the moment the business does, before anything is
-- accepted. A row written before this migration keeps the words it has. The acceptance is its own table, kept forever like the others:
-- a trigger refuses every UPDATE, DELETE and TRUNCATE, a tenant that has an acceptance cannot be deleted (ON DELETE RESTRICT), and a
-- tenant accepts once (UNIQUE). What was accepted is the exact sentence shown, with when it was accepted and the visitor's address and
-- country, as the other signup records hold them.

ALTER TABLE geek_voice.signup_attestations ALTER COLUMN terms_statement DROP NOT NULL;

CREATE TABLE IF NOT EXISTS geek_voice.signup_terms_acceptances (
    id              UUID        PRIMARY KEY,
    tenant_id       UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE RESTRICT,
    statement       TEXT        NOT NULL,
    accepted_at     TIMESTAMPTZ NOT NULL,
    ip_address      INET        NOT NULL,
    visitor_country TEXT        NOT NULL,
    CONSTRAINT uq_voice_signup_terms_acceptances_tenant UNIQUE (tenant_id),
    CONSTRAINT ck_voice_signup_terms_acceptances_statement CHECK (statement !~ '^\s*$'),
    CONSTRAINT ck_voice_signup_terms_acceptances_country CHECK (visitor_country !~ '^\s*$')
);

CREATE OR REPLACE FUNCTION geek_voice.refuse_signup_terms_acceptance_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'geek_voice.signup_terms_acceptances is kept forever: % is refused', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS trg_voice_signup_terms_acceptances_keep_rows ON geek_voice.signup_terms_acceptances;
CREATE TRIGGER trg_voice_signup_terms_acceptances_keep_rows
    BEFORE UPDATE OR DELETE ON geek_voice.signup_terms_acceptances
    FOR EACH ROW EXECUTE FUNCTION geek_voice.refuse_signup_terms_acceptance_change();

DROP TRIGGER IF EXISTS trg_voice_signup_terms_acceptances_keep_table ON geek_voice.signup_terms_acceptances;
CREATE TRIGGER trg_voice_signup_terms_acceptances_keep_table
    BEFORE TRUNCATE ON geek_voice.signup_terms_acceptances
    FOR EACH STATEMENT EXECUTE FUNCTION geek_voice.refuse_signup_terms_acceptance_change();
