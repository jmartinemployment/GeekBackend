-- 0056: Geek-CRM Phase 3, what the AI voice receptionist needs (docs/GEEK-CRM-DATA-MODEL.md, 4.7, 4.9, 4.16).
--
-- Estimate detail on a deal's line items and when the estimate was given; a booked-meeting slot and a
-- priority on activities (a meeting is an activity, there is no meetings table); a GIN index on a deal's
-- qualification answers; and projects, whose hours set delivery capacity (docs 4.16). Projects carry
-- tenant_id, UNIQUE (tenant_id, id), composite foreign keys and forced row-level security like every
-- tenant table. Declared in GeekApplication/Schemas/GeekCrm.xsd. Idempotent.

ALTER TABLE geek_crm.deals ADD COLUMN IF NOT EXISTS estimate_given_at timestamptz NULL;
CREATE INDEX IF NOT EXISTS ix_deals_qualification ON geek_crm.deals USING gin (qualification);

ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS size text NULL CONSTRAINT ck_deal_line_items_size CHECK (size IS NULL OR size IN ('small', 'medium', 'large'));
ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS hours numeric(7, 2) NULL CONSTRAINT ck_deal_line_items_hours CHECK (hours >= 0);
ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS hourly_rate numeric(12, 2) NULL CONSTRAINT ck_deal_line_items_hourly_rate CHECK (hourly_rate >= 0);
ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS demand_uplift_pct numeric(5, 2) NULL CONSTRAINT ck_deal_line_items_uplift CHECK (demand_uplift_pct BETWEEN 0 AND 100);
ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS is_estimate boolean NULL;
ALTER TABLE geek_crm.deal_line_items ADD COLUMN IF NOT EXISTS estimate_inputs jsonb NULL;

ALTER TABLE geek_crm.activities ADD COLUMN IF NOT EXISTS starts_at timestamptz NULL;
ALTER TABLE geek_crm.activities ADD COLUMN IF NOT EXISTS ends_at timestamptz NULL;
ALTER TABLE geek_crm.activities ADD COLUMN IF NOT EXISTS calendar_event_id text NULL;
ALTER TABLE geek_crm.activities ADD COLUMN IF NOT EXISTS priority text NULL CONSTRAINT ck_activities_priority CHECK (priority IS NULL OR priority IN ('normal', 'urgent'));
CREATE INDEX IF NOT EXISTS ix_activities_urgent ON geek_crm.activities (tenant_id, occurred_at DESC) WHERE priority = 'urgent';

CREATE TABLE IF NOT EXISTS geek_crm.projects (
    id                   uuid PRIMARY KEY,
    tenant_id            uuid NOT NULL REFERENCES geek_crm.tenants (id),
    participant_id       uuid NOT NULL,
    deal_id              uuid NULL,
    customer_holding_id  uuid NULL,
    status               text NOT NULL CONSTRAINT ck_projects_status CHECK (status IN ('scheduled', 'active', 'on_hold', 'done', 'cancelled')),
    start_on             date NULL,
    target_end_on        date NULL,
    estimated_hours      numeric(7, 2) NOT NULL CONSTRAINT ck_projects_estimated_hours CHECK (estimated_hours >= 0),
    hours_per_week       numeric(7, 2) NOT NULL CONSTRAINT ck_projects_hours_per_week CHECK (hours_per_week >= 0),
    created_at           timestamptz NOT NULL,
    updated_at           timestamptz NOT NULL,
    CONSTRAINT ck_projects_period CHECK (target_end_on IS NULL OR start_on IS NULL OR target_end_on >= start_on)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_tenant_id ON geek_crm.projects (tenant_id, id);
CREATE INDEX IF NOT EXISTS ix_projects_capacity ON geek_crm.projects (tenant_id, status, start_on);

ALTER TABLE geek_crm.projects DROP CONSTRAINT IF EXISTS fk_projects_participant_id_tenant;
ALTER TABLE geek_crm.projects ADD CONSTRAINT fk_projects_participant_id_tenant
    FOREIGN KEY (tenant_id, participant_id) REFERENCES geek_crm.participants (tenant_id, id);

ALTER TABLE geek_crm.projects DROP CONSTRAINT IF EXISTS fk_projects_deal_id_tenant;
ALTER TABLE geek_crm.projects ADD CONSTRAINT fk_projects_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE SET NULL (deal_id);

ALTER TABLE geek_crm.projects DROP CONSTRAINT IF EXISTS fk_projects_customer_holding_id_tenant;
ALTER TABLE geek_crm.projects ADD CONSTRAINT fk_projects_customer_holding_id_tenant
    FOREIGN KEY (tenant_id, customer_holding_id) REFERENCES geek_crm.customer_holdings (tenant_id, id) ON DELETE SET NULL (customer_holding_id);

ALTER TABLE geek_crm.projects ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.projects FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.projects;
CREATE POLICY tenant_isolation ON geek_crm.projects
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
