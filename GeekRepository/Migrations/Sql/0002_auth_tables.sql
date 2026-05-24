CREATE TABLE IF NOT EXISTS jti_blacklist (
    jti TEXT PRIMARY KEY,
    user_id UUID,
    expires_at TIMESTAMPTZ NOT NULL,
    blacklisted_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_jti_blacklist_expires_at ON jti_blacklist (expires_at);

CREATE TABLE IF NOT EXISTS two_factor_pending_sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    session_code TEXT NOT NULL UNIQUE,
    device_fingerprint TEXT NOT NULL,
    attempt_count INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMPTZ NOT NULL,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS ix_two_factor_pending_sessions_expires
    ON two_factor_pending_sessions (expires_at);
