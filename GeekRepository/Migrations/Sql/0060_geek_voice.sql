-- Geek-Receptionist v1 (docs/Geek-Receptionist.md section 3): the geek_voice schema. One row set per tenant, in nine tables.
-- It stands alone: nothing here references geek_crm, geek_social or any other schema.
-- Every tenant-scoped table carries tenant_id and a row-level-security policy keyed on the session setting
-- app.current_tenant_id, which GeekRepository sets on every connection it opens for a tenant. Every query also names its tenant
-- in a WHERE clause, so the policy is the second guard and the clause is the first. A superuser or a BYPASSRLS login skips the
-- policy even on a FORCEd table; the WHERE clause is then the only guard.
-- Encrypted columns hold ciphertext produced by the caller. GeekRepository never sees a key and never decrypts.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS geek_voice;

CREATE TABLE IF NOT EXISTS geek_voice.tenants (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_name           TEXT        NOT NULL,
    industry_vertical       TEXT        NOT NULL,
    requires_addendum       BOOLEAN     NOT NULL DEFAULT FALSE,
    addendum_signed_at      TIMESTAMPTZ NULL,
    addendum_ip_address     INET        NULL,
    compliance_attested_at  TIMESTAMPTZ NOT NULL,
    country_code            CHAR(2)     NOT NULL CHECK (country_code = 'US'),
    state_code              CHAR(2)     NOT NULL,
    county                  TEXT        NULL,
    contact_email           TEXT        NOT NULL,
    contact_phone           TEXT        NOT NULL,
    status                  TEXT        NOT NULL DEFAULT 'pending_payment'
                            CHECK (status IN ('pending_payment', 'active', 'suspended', 'cancelled')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_voice_tenants_addendum_pair CHECK ((addendum_signed_at IS NULL) = (addendum_ip_address IS NULL))
);

CREATE TABLE IF NOT EXISTS geek_voice.tenant_subscriptions (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                UUID          NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    tier_code                TEXT          NOT NULL CHECK (tier_code IN ('STARTER', 'STANDARD', 'PRO', 'PREMIUM')),
    monthly_price_usd        NUMERIC(10,2) NOT NULL CHECK (monthly_price_usd >= 0),
    included_minutes         INT           NOT NULL CHECK (included_minutes >= 0),
    overage_rate_usd         NUMERIC(10,2) NOT NULL DEFAULT 0.50 CHECK (overage_rate_usd >= 0),
    paypal_subscription_id   TEXT          NOT NULL UNIQUE,
    paypal_vault_token_id    TEXT          NOT NULL,
    current_period_start     TIMESTAMPTZ   NOT NULL,
    current_period_end       TIMESTAMPTZ   NOT NULL,
    billing_status           TEXT          NOT NULL,
    created_at               TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_voice_subscriptions_period CHECK (current_period_end > current_period_start)
);

CREATE INDEX IF NOT EXISTS ix_voice_subscriptions_tenant ON geek_voice.tenant_subscriptions (tenant_id);

CREATE TABLE IF NOT EXISTS geek_voice.tenant_telephony (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                   UUID        NOT NULL UNIQUE REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    twilio_subaccount_sid       TEXT        NOT NULL,
    twilio_auth_token_encrypted TEXT        NOT NULL,
    assigned_phone_number       TEXT        NOT NULL UNIQUE,
    twilio_phone_sid            TEXT        NOT NULL,
    retell_agent_id             TEXT        NULL,
    a2p_brand_sid               TEXT        NULL,
    a2p_campaign_sid            TEXT        NULL,
    a2p_status                  TEXT        NOT NULL,
    sms_notifications_enabled   BOOLEAN     NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS geek_voice.receptionist_configs (
    id                              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       UUID        NOT NULL UNIQUE REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    greeting_message                TEXT        NOT NULL,
    voice_id                        TEXT        NOT NULL,
    system_prompt_template          TEXT        NOT NULL,
    business_hours_json             JSONB       NOT NULL,
    qualifying_questions_json       JSONB       NOT NULL,
    transfer_mode                   TEXT        NOT NULL
                                    CHECK (transfer_mode IN ('warm_transfer', 'cold_transfer', 'voicemail_only')),
    transfer_target_number          TEXT        NULL,
    transfer_caller_id_mode         TEXT        NOT NULL
                                    CHECK (transfer_caller_id_mode IN ('original_caller', 'assigned_ai_number')),
    emergency_keywords              TEXT[]      NOT NULL,
    recording_disclosure_enabled    BOOLEAN     NOT NULL DEFAULT TRUE,
    updated_at                      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS geek_voice.tenant_mcp_connections (
    id                              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    provider                        TEXT        NOT NULL
                                    CHECK (provider IN ('google_calendar', 'outlook', 'cal_com', 'hubspot')),
    oauth_access_token_encrypted    TEXT        NOT NULL,
    oauth_refresh_token_encrypted   TEXT        NOT NULL,
    token_expires_at                TIMESTAMPTZ NULL,
    external_calendar_id            TEXT        NULL,
    is_active                       BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_voice_mcp_tenant_provider UNIQUE (tenant_id, provider)
);

CREATE TABLE IF NOT EXISTS geek_voice.knowledge_documents (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    source_type   TEXT        NOT NULL CHECK (source_type IN ('guided_trade_form', 'pdf_upload', 'geek_crawler_url')),
    title         TEXT        NOT NULL,
    source_url    TEXT        NULL,
    raw_content   TEXT        NOT NULL,
    status        TEXT        NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_voice_documents_tenant ON geek_voice.knowledge_documents (tenant_id);

-- 384 dimensions: BAAI/bge-small-en-v1.5. Search runs inside one tenant's rows, so it is an exact scan on the tenant index.
-- An approximate-nearest-neighbour index filters after it searches and can return fewer rows than asked for, so none is created.
CREATE TABLE IF NOT EXISTS geek_voice.knowledge_chunks (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    document_id   UUID        NOT NULL REFERENCES geek_voice.knowledge_documents(id) ON DELETE CASCADE,
    chunk_index   INT         NOT NULL CHECK (chunk_index >= 0),
    chunk_text    TEXT        NOT NULL,
    embedding     vector(384) NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_voice_chunks_document_index UNIQUE (document_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS ix_voice_chunks_tenant ON geek_voice.knowledge_chunks (tenant_id);

CREATE TABLE IF NOT EXISTS geek_voice.call_logs (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    twilio_call_sid    TEXT        NOT NULL UNIQUE,
    caller_number      TEXT        NOT NULL,
    call_status        TEXT        NOT NULL,
    started_at         TIMESTAMPTZ NOT NULL,
    ended_at           TIMESTAMPTZ NULL,
    duration_seconds   INT         NOT NULL DEFAULT 0 CHECK (duration_seconds >= 0),
    billable_minutes   INT         NOT NULL DEFAULT 0 CHECK (billable_minutes >= 0),
    transcript_json    JSONB       NOT NULL DEFAULT '[]'::jsonb,
    call_summary       TEXT        NULL,
    disposition        TEXT        NULL,
    recording_url      TEXT        NULL
);

CREATE INDEX IF NOT EXISTS ix_voice_calls_tenant_started ON geek_voice.call_logs (tenant_id, started_at DESC);

CREATE TABLE IF NOT EXISTS geek_voice.appointments (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID        NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    call_log_id         UUID        NULL REFERENCES geek_voice.call_logs(id) ON DELETE SET NULL,
    customer_name       TEXT        NOT NULL,
    customer_phone      TEXT        NOT NULL,
    customer_email      TEXT        NULL,
    service_address     TEXT        NOT NULL,
    issue_description   TEXT        NOT NULL,
    scheduled_start     TIMESTAMPTZ NOT NULL,
    scheduled_end       TIMESTAMPTZ NOT NULL,
    external_event_id   TEXT        NULL,
    mcp_provider        TEXT        NOT NULL
                        CHECK (mcp_provider IN ('google_calendar', 'outlook', 'cal_com', 'hubspot')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_voice_appointments_window CHECK (scheduled_end > scheduled_start)
);

CREATE INDEX IF NOT EXISTS ix_voice_appointments_tenant_start ON geek_voice.appointments (tenant_id, scheduled_start);

-- Row-level security. NULLIF turns an unset or empty setting into NULL, so the policy matches no row instead of failing the cast.
ALTER TABLE geek_voice.tenants                ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_subscriptions   ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_telephony       ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.receptionist_configs   ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_mcp_connections ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.knowledge_documents    ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.knowledge_chunks       ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.call_logs              ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.appointments           ENABLE ROW LEVEL SECURITY;

ALTER TABLE geek_voice.tenants                FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_subscriptions   FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_telephony       FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.receptionist_configs   FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.tenant_mcp_connections FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.knowledge_documents    FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.knowledge_chunks       FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.call_logs              FORCE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.appointments           FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.tenants;
CREATE POLICY tenant_isolation ON geek_voice.tenants
    USING (id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.tenant_subscriptions;
CREATE POLICY tenant_isolation ON geek_voice.tenant_subscriptions
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.tenant_telephony;
CREATE POLICY tenant_isolation ON geek_voice.tenant_telephony
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.receptionist_configs;
CREATE POLICY tenant_isolation ON geek_voice.receptionist_configs
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.tenant_mcp_connections;
CREATE POLICY tenant_isolation ON geek_voice.tenant_mcp_connections
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.knowledge_documents;
CREATE POLICY tenant_isolation ON geek_voice.knowledge_documents
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.knowledge_chunks;
CREATE POLICY tenant_isolation ON geek_voice.knowledge_chunks
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.call_logs;
CREATE POLICY tenant_isolation ON geek_voice.call_logs
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.appointments;
CREATE POLICY tenant_isolation ON geek_voice.appointments
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
