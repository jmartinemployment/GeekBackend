-- GeekSocial, roadmap Phase 0 item 3: a step GeekSocial could not complete is kept here, so the tenant's Activity page
-- shows it after a restart, instead of a list of 20 held in GeekAPI's memory and shared by every tenant.
--
-- tenant_id is the tenant whose comment or draft the step was working on. It is null for a step that belongs to no one
-- tenant (startup recovery, the retention purge, a list across tenants); every tenant sees those, with the step, the
-- code and the time only. A row holds no comment text, draft text or person's data. Rows are deleted 30 days after
-- they occur by the same purge call that scrubs expired text. Idempotent.

CREATE TABLE IF NOT EXISTS geek_social.step_failures (
    id          uuid PRIMARY KEY,
    tenant_id   uuid NULL REFERENCES geek_social.tenants (id),
    step        text NOT NULL,
    code        text NOT NULL,
    occurred_at timestamptz NOT NULL,
    CONSTRAINT ck_step_failures_step CHECK (length(btrim(step)) BETWEEN 1 AND 100),
    CONSTRAINT ck_step_failures_code CHECK (code ~ '^[a-z_]{1,64}$')
);

CREATE INDEX IF NOT EXISTS ix_step_failures_tenant_occurred ON geek_social.step_failures (tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_step_failures_occurred ON geek_social.step_failures (occurred_at);
