-- Geek-Receptionist owner logins (docs/Geek-Receptionist.md section 7): the account a tenant's owner signs in with, and the refresh
-- tokens that keep them signed in. They belong to Geek-Receptionist alone and are not in the shared GeekOAuth store, so an owner's
-- login can never be accepted by another Geek app. GeekAPI hashes the password and the refresh tokens before they reach GeekRepository,
-- which never sees a password, a token or a key.
-- These two tables have no row-level security: a login is looked up by email before any tenant is known, so there is no tenant setting
-- to key a policy on. Every statement names its row, and GeekRepository is reached only with X-Internal-Api-Key.

CREATE TABLE IF NOT EXISTS geek_voice.owner_logins (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID        NOT NULL UNIQUE REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    email         TEXT        NOT NULL,
    password_hash TEXT        NOT NULL,
    display_name  TEXT        NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_voice_owner_logins_email CHECK (email = lower(email) AND char_length(email) BETWEEN 3 AND 254)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_voice_owner_logins_email ON geek_voice.owner_logins (email);

CREATE TABLE IF NOT EXISTS geek_voice.owner_refresh_tokens (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_login_id UUID        NOT NULL REFERENCES geek_voice.owner_logins(id) ON DELETE CASCADE,
    family_id      UUID        NOT NULL,
    token_hash     TEXT        NOT NULL UNIQUE,
    issued_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at     TIMESTAMPTZ NOT NULL,
    used_at        TIMESTAMPTZ NULL,
    revoked_at     TIMESTAMPTZ NULL,
    CONSTRAINT ck_voice_owner_refresh_window CHECK (expires_at > issued_at)
);

CREATE INDEX IF NOT EXISTS ix_voice_owner_refresh_family ON geek_voice.owner_refresh_tokens (family_id);
CREATE INDEX IF NOT EXISTS ix_voice_owner_refresh_login ON geek_voice.owner_refresh_tokens (owner_login_id);
