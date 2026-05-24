CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE IF NOT EXISTS openiddict_applications (
    id TEXT PRIMARY KEY,
    application_type TEXT,
    client_id TEXT,
    client_secret TEXT,
    client_type TEXT,
    consent_type TEXT,
    display_name TEXT,
    display_names TEXT,
    json_web_key_set TEXT,
    permissions TEXT,
    post_logout_redirect_uris TEXT,
    properties TEXT,
    redirect_uris TEXT,
    requirements TEXT,
    settings TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_openiddict_applications_client_id
    ON openiddict_applications (client_id)
    WHERE client_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS openiddict_scopes (
    id TEXT PRIMARY KEY,
    description TEXT,
    descriptions TEXT,
    display_name TEXT,
    display_names TEXT,
    name TEXT,
    properties TEXT,
    resources TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_openiddict_scopes_name
    ON openiddict_scopes (name)
    WHERE name IS NOT NULL;

CREATE TABLE IF NOT EXISTS openiddict_authorizations (
    id TEXT PRIMARY KEY,
    application_id TEXT REFERENCES openiddict_applications(id) ON DELETE CASCADE,
    concurrency_token TEXT,
    creation_date TIMESTAMPTZ,
    properties TEXT,
    scopes TEXT,
    status TEXT,
    subject TEXT,
    type TEXT
);

CREATE TABLE IF NOT EXISTS openiddict_tokens (
    id TEXT PRIMARY KEY,
    application_id TEXT REFERENCES openiddict_applications(id) ON DELETE CASCADE,
    authorization_id TEXT REFERENCES openiddict_authorizations(id) ON DELETE CASCADE,
    concurrency_token TEXT,
    creation_date TIMESTAMPTZ,
    expiration_date TIMESTAMPTZ,
    payload TEXT,
    properties TEXT,
    redemption_date TIMESTAMPTZ,
    reference_id TEXT,
    status TEXT,
    subject TEXT,
    type TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_openiddict_tokens_reference_id
    ON openiddict_tokens (reference_id)
    WHERE reference_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS jti_blacklist (
    jti TEXT PRIMARY KEY,
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_jti_blacklist_expires_at ON jti_blacklist (expires_at);
