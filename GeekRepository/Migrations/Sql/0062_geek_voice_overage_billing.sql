-- Geek-Receptionist end-of-cycle billing (docs/Geek-Receptionist.md section 5, item 2).

-- 1. Calls are keyed by Retell's call id. Retell posts every call to GeekAPI with its call_id, and gives the Twilio call SID only
--    "when available", so the SID becomes optional and the Retell id is what a repeated delivery is matched on. The NOT VALID check
--    holds every row written from now on without reading the rows already there; GeekAPI wrote none before this script, and a
--    check that had to scan them could only stop GeekRepository from starting.
ALTER TABLE geek_voice.call_logs ADD COLUMN IF NOT EXISTS retell_call_id TEXT NULL;
ALTER TABLE geek_voice.call_logs ALTER COLUMN twilio_call_sid DROP NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_calls_retell_call_id') THEN
        ALTER TABLE geek_voice.call_logs ADD CONSTRAINT ck_voice_calls_retell_call_id CHECK (retell_call_id IS NOT NULL) NOT VALID;
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_voice_calls_retell_call ON geek_voice.call_logs (retell_call_id);

-- 2. One row per closed billing period: the minutes used, the overage and the charge made for it. The unique key closes a period
--    once, whichever GeekAPI instance gets there first. Only 'charging' is unsettled; the PayPal ids belong to 'charged' and the
--    reason to 'failed'.
CREATE TABLE IF NOT EXISTS geek_voice.overage_charges (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          UUID          NOT NULL REFERENCES geek_voice.tenants(id) ON DELETE CASCADE,
    subscription_id    UUID          NOT NULL REFERENCES geek_voice.tenant_subscriptions(id) ON DELETE CASCADE,
    period_start       TIMESTAMPTZ   NOT NULL,
    period_end         TIMESTAMPTZ   NOT NULL,
    included_minutes   INT           NOT NULL CHECK (included_minutes >= 0),
    used_minutes       INT           NOT NULL CHECK (used_minutes >= 0),
    overage_minutes    INT           NOT NULL CHECK (overage_minutes >= 0),
    overage_rate_usd   NUMERIC(10,2) NOT NULL CHECK (overage_rate_usd >= 0),
    amount_usd         NUMERIC(10,2) NOT NULL CHECK (amount_usd >= 0),
    status             TEXT          NOT NULL CHECK (status IN ('no_overage', 'charging', 'charged', 'failed')),
    paypal_order_id    TEXT          NULL,
    paypal_capture_id  TEXT          NULL,
    failure            TEXT          NULL,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    settled_at         TIMESTAMPTZ   NULL,
    CONSTRAINT uq_voice_overage_period UNIQUE (subscription_id, period_start),
    CONSTRAINT ck_voice_overage_window CHECK (period_end > period_start),
    CONSTRAINT ck_voice_overage_none CHECK ((status = 'no_overage') = (overage_minutes = 0)),
    CONSTRAINT ck_voice_overage_settled CHECK ((status = 'charging') = (settled_at IS NULL)),
    CONSTRAINT ck_voice_overage_charged CHECK (status <> 'charged' OR (paypal_order_id IS NOT NULL AND paypal_capture_id IS NOT NULL)),
    CONSTRAINT ck_voice_overage_failed CHECK (status <> 'failed' OR failure IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_voice_overage_tenant ON geek_voice.overage_charges (tenant_id, period_start DESC);

ALTER TABLE geek_voice.overage_charges ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_voice.overage_charges FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON geek_voice.overage_charges;
CREATE POLICY tenant_isolation ON geek_voice.overage_charges
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- 3. The billing sweep has to find the periods that ended before it knows any tenant. This policy lets a transaction that sets
--    app.billing_sweep to 'on' read every subscription row, and nothing else: it is FOR SELECT, on this table only, and the sweep
--    reads only the tenant id, the subscription id and the period end. Everything it then does to a tenant runs on a connection that
--    names that tenant. Setting it takes the same X-Internal-Api-Key-guarded access that setting app.current_tenant_id takes.
DROP POLICY IF EXISTS billing_sweep ON geek_voice.tenant_subscriptions;
CREATE POLICY billing_sweep ON geek_voice.tenant_subscriptions FOR SELECT
    USING (current_setting('app.billing_sweep', true) = 'on');

CREATE INDEX IF NOT EXISTS ix_voice_subscriptions_period_end ON geek_voice.tenant_subscriptions (current_period_end) WHERE billing_status = 'ACTIVE';
