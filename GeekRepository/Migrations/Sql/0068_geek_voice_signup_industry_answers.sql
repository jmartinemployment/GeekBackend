-- Geek-Receptionist signup, decided by Jeff 2026-11-18: the answer to each excluded-industry question is kept forever, the
-- healthcare and veterinary questions included. One row per question answered: the industry, the exact words shown, the answer,
-- when, and the visitor's address and country. tenant_id is null for a visitor who answered yes and was refused, and is the new
-- tenant for a business that answered no to every question (written in the same transaction as the tenant).
--
-- "Forever" is enforced here and not only written down: a trigger refuses every UPDATE, DELETE and TRUNCATE on the table, and a
-- tenant that has answers cannot be deleted (ON DELETE RESTRICT). No code deletes from it.
--
-- The table is read before a tenant exists, so it has no row-level-security policy. Nothing serves its rows to a tenant.

CREATE TABLE IF NOT EXISTS geek_voice.signup_industry_answers (
    id              UUID        PRIMARY KEY,
    tenant_id       UUID        NULL REFERENCES geek_voice.tenants(id) ON DELETE RESTRICT,
    industry        TEXT        NOT NULL,
    question        TEXT        NOT NULL,
    answer          BOOLEAN     NOT NULL,
    answered_at     TIMESTAMPTZ NOT NULL,
    ip_address      INET        NOT NULL,
    visitor_country TEXT        NOT NULL,
    CONSTRAINT ck_voice_signup_industry_answers_industry CHECK (btrim(industry) <> ''),
    CONSTRAINT ck_voice_signup_industry_answers_question CHECK (btrim(question) <> ''),
    CONSTRAINT ck_voice_signup_industry_answers_country CHECK (btrim(visitor_country) <> ''),
    -- A business that became a tenant answered no to every question; a yes is never a tenant's.
    CONSTRAINT ck_voice_signup_industry_answers_tenant_said_no CHECK (tenant_id IS NULL OR answer = FALSE),
    CONSTRAINT uq_voice_signup_industry_answers_tenant_industry UNIQUE (tenant_id, industry)
);

CREATE INDEX IF NOT EXISTS ix_voice_signup_industry_answers_answered_at ON geek_voice.signup_industry_answers (answered_at);

CREATE OR REPLACE FUNCTION geek_voice.refuse_signup_industry_answer_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'geek_voice.signup_industry_answers is kept forever: % is refused', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS trg_voice_signup_industry_answers_keep_rows ON geek_voice.signup_industry_answers;
CREATE TRIGGER trg_voice_signup_industry_answers_keep_rows
    BEFORE UPDATE OR DELETE ON geek_voice.signup_industry_answers
    FOR EACH ROW EXECUTE FUNCTION geek_voice.refuse_signup_industry_answer_change();

DROP TRIGGER IF EXISTS trg_voice_signup_industry_answers_keep_table ON geek_voice.signup_industry_answers;
CREATE TRIGGER trg_voice_signup_industry_answers_keep_table
    BEFORE TRUNCATE ON geek_voice.signup_industry_answers
    FOR EACH STATEMENT EXECUTE FUNCTION geek_voice.refuse_signup_industry_answer_change();
