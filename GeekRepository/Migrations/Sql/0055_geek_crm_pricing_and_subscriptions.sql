-- 0055: Geek-CRM Phase 2, the rest: one uplift tier per booked level, and tenant_subscriptions.
--
-- tenant_subscriptions records which tenant may use which multi-tenant Geek product (Geek-Receptionist
-- first). It belongs to the platform, not to a tenant, so it has no tenant_id and a different gate: forced
-- row-level security whose only policy asks for app.platform = 'on', which GeekRepository sets only for a
-- platform operation (CrmTenantScope.RunPlatformAsync) reached through repo/platform/* with the
-- platform-admin key. The same policy lets that operation read the tenants table, so a subscriber can be
-- checked. A tenant-scoped operation can never read a subscription.
-- Declared in GeekApplication/Schemas/GeekCrm.xsd. Idempotent.

CREATE TABLE IF NOT EXISTS geek_crm.tenant_subscriptions (
    id                      uuid PRIMARY KEY,
    subscriber_tenant_id    uuid NOT NULL REFERENCES geek_crm.tenants (id),
    product_code            text NOT NULL,
    plan                    text NULL,
    status                  text NOT NULL CONSTRAINT ck_tenant_subscriptions_status CHECK (status IN ('proposed', 'active', 'paused', 'cancelled', 'ended')),
    start_on                date NOT NULL,
    end_on                  date NULL,
    created_at              timestamptz NOT NULL,
    CONSTRAINT ck_tenant_subscriptions_period CHECK (end_on IS NULL OR end_on >= start_on)
);

CREATE INDEX IF NOT EXISTS ix_tenant_subscriptions_subscriber ON geek_crm.tenant_subscriptions (subscriber_tenant_id, product_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_pricing_uplift_tiers_tenant_booked ON geek_crm.pricing_uplift_tiers (tenant_id, booked_pct_from);

ALTER TABLE geek_crm.tenant_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.tenant_subscriptions FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS platform_access ON geek_crm.tenant_subscriptions;
CREATE POLICY platform_access ON geek_crm.tenant_subscriptions
    USING (nullif(current_setting('app.platform', true), '') = 'on')
    WITH CHECK (nullif(current_setting('app.platform', true), '') = 'on');

DROP POLICY IF EXISTS platform_access ON geek_crm.tenants;
CREATE POLICY platform_access ON geek_crm.tenants
    USING (nullif(current_setting('app.platform', true), '') = 'on')
    WITH CHECK (nullif(current_setting('app.platform', true), '') = 'on');
