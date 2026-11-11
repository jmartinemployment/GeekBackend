-- 0054: Geek-CRM Phase 2, Product (docs/GEEK-CRM-DATA-MODEL.md, 4.12 to 4.14).
--
-- A per-tenant catalog (products, product_prices, pricing_rules, pricing_uplift_tiers), what each
-- customer holds (customer_holdings), and a deal's line items (deal_line_items). Every table carries
-- tenant_id, UNIQUE (tenant_id, id), composite foreign keys and forced row-level security, the same
-- boundary 0053 gave the v0 tables. Products are retired (is_active = false), never deleted; a price
-- may be blank until it is set and carries its dates; a holding keeps the quantity, price and
-- discount it was sold at, and its deal_line_item_id is unique per tenant so a repeated win creates
-- nothing. tenant_subscriptions is platform-scoped and belongs with the multi-tenant work, not here;
-- customer_holdings.tenant_subscription_id is a plain column until then.
-- Declared in GeekApplication/Schemas/GeekCrm.xsd. Idempotent.

CREATE TABLE IF NOT EXISTS geek_crm.products (
    id                       uuid PRIMARY KEY,
    tenant_id                uuid NOT NULL REFERENCES geek_crm.tenants (id),
    name                     text NOT NULL,
    code                     text NOT NULL,
    kind                     text NOT NULL CONSTRAINT ck_products_kind CHECK (kind IN ('geek_saas', 'implementation', 'partner_subscription', 'support_plan', 'service')),
    category                 text NOT NULL CONSTRAINT ck_products_category CHECK (category IN ('accounting', 'business_operations', 'marketing', 'sales', 'bespoke')),
    use_case_path            text NULL,
    partner_participant_id   uuid NULL,
    billing                  text NOT NULL CONSTRAINT ck_products_billing CHECK (billing IN ('one_time', 'monthly', 'annual')),
    is_active                boolean NOT NULL DEFAULT true,
    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS geek_crm.product_prices (
    id                       uuid PRIMARY KEY,
    tenant_id                uuid NOT NULL REFERENCES geek_crm.tenants (id),
    product_id               uuid NOT NULL,
    size                     text NULL CONSTRAINT ck_product_prices_size CHECK (size IS NULL OR size IN ('small', 'medium', 'large')),
    hours                    numeric(7, 2) NULL CONSTRAINT ck_product_prices_hours CHECK (hours >= 0),
    unit_price               numeric(12, 2) NULL CONSTRAINT ck_product_prices_unit_price CHECK (unit_price >= 0),
    recurring_price_monthly  numeric(12, 2) NULL CONSTRAINT ck_product_prices_recurring CHECK (recurring_price_monthly >= 0),
    currency                 text NOT NULL DEFAULT 'USD' CONSTRAINT ck_product_prices_currency CHECK (currency IN ('USD')),
    effective_from           date NOT NULL,
    effective_to             date NULL,
    created_at               timestamptz NOT NULL,
    CONSTRAINT ck_product_prices_period CHECK (effective_to IS NULL OR effective_to >= effective_from)
);

CREATE TABLE IF NOT EXISTS geek_crm.pricing_rules (
    id                       uuid PRIMARY KEY,
    tenant_id                uuid NOT NULL REFERENCES geek_crm.tenants (id),
    hourly_rate              numeric(12, 2) NULL CONSTRAINT ck_pricing_rules_hourly_rate CHECK (hourly_rate >= 0),
    related_use_case_pct     numeric(5, 2) NOT NULL DEFAULT 60 CONSTRAINT ck_pricing_rules_related_pct CHECK (related_use_case_pct BETWEEN 0 AND 100),
    currency                 text NOT NULL DEFAULT 'USD' CONSTRAINT ck_pricing_rules_currency CHECK (currency IN ('USD')),
    updated_at               timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS geek_crm.pricing_uplift_tiers (
    id                       uuid PRIMARY KEY,
    tenant_id                uuid NOT NULL REFERENCES geek_crm.tenants (id),
    booked_pct_from          numeric(5, 2) NOT NULL CONSTRAINT ck_pricing_uplift_tiers_booked CHECK (booked_pct_from BETWEEN 0 AND 100),
    uplift_pct               numeric(5, 2) NOT NULL CONSTRAINT ck_pricing_uplift_tiers_uplift CHECK (uplift_pct BETWEEN 0 AND 100),
    created_at               timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS geek_crm.deal_line_items (
    id                       uuid PRIMARY KEY,
    tenant_id                uuid NOT NULL REFERENCES geek_crm.tenants (id),
    deal_id                  uuid NOT NULL,
    product_id               uuid NOT NULL,
    quantity                 numeric(10, 2) NOT NULL CONSTRAINT ck_deal_line_items_quantity CHECK (quantity > 0),
    unit_price               numeric(12, 2) NOT NULL CONSTRAINT ck_deal_line_items_unit_price CHECK (unit_price >= 0),
    is_recurring             boolean NOT NULL,
    discount_pct             numeric(5, 2) NOT NULL DEFAULT 0 CONSTRAINT ck_deal_line_items_discount CHECK (discount_pct BETWEEN 0 AND 100),
    currency                 text NOT NULL DEFAULT 'USD' CONSTRAINT ck_deal_line_items_currency CHECK (currency IN ('USD')),
    created_at               timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS geek_crm.customer_holdings (
    id                         uuid PRIMARY KEY,
    tenant_id                  uuid NOT NULL REFERENCES geek_crm.tenants (id),
    participant_id             uuid NOT NULL,
    product_id                 uuid NOT NULL,
    deal_id                    uuid NULL,
    deal_line_item_id          uuid NULL,
    status                     text NOT NULL CONSTRAINT ck_customer_holdings_status CHECK (status IN ('proposed', 'active', 'paused', 'cancelled', 'ended')),
    start_on                   date NULL,
    end_on                     date NULL,
    quantity                   numeric(10, 2) NOT NULL CONSTRAINT ck_customer_holdings_quantity CHECK (quantity > 0),
    unit_price                 numeric(12, 2) NOT NULL CONSTRAINT ck_customer_holdings_unit_price CHECK (unit_price >= 0),
    discount_pct               numeric(5, 2) NOT NULL DEFAULT 0 CONSTRAINT ck_customer_holdings_discount CHECK (discount_pct BETWEEN 0 AND 100),
    recurring_amount_monthly   numeric(12, 2) NULL CONSTRAINT ck_customer_holdings_recurring CHECK (recurring_amount_monthly >= 0),
    one_time_amount            numeric(12, 2) NULL CONSTRAINT ck_customer_holdings_one_time CHECK (one_time_amount >= 0),
    currency                   text NOT NULL DEFAULT 'USD' CONSTRAINT ck_customer_holdings_currency CHECK (currency IN ('USD')),
    arrangement                text NULL CONSTRAINT ck_customer_holdings_arrangement CHECK (arrangement IS NULL OR arrangement IN ('customer_direct', 'referral', 'resold')),
    referral_fee               numeric(12, 2) NULL CONSTRAINT ck_customer_holdings_referral_fee CHECK (referral_fee >= 0),
    referral_fee_is_recurring  boolean NULL,
    referral_paid_on           date NULL,
    partner_cost_monthly       numeric(12, 2) NULL CONSTRAINT ck_customer_holdings_partner_cost CHECK (partner_cost_monthly >= 0),
    external_ref               text NULL,
    tenant_subscription_id     uuid NULL,
    created_at                 timestamptz NOT NULL,
    updated_at                 timestamptz NOT NULL,
    CONSTRAINT ck_customer_holdings_period CHECK (end_on IS NULL OR start_on IS NULL OR end_on >= start_on)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_products_tenant_id ON geek_crm.products (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_prices_tenant_id ON geek_crm.product_prices (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pricing_rules_tenant_id ON geek_crm.pricing_rules (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pricing_uplift_tiers_tenant_id ON geek_crm.pricing_uplift_tiers (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_deal_line_items_tenant_id ON geek_crm.deal_line_items (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_holdings_tenant_id ON geek_crm.customer_holdings (tenant_id, id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_products_tenant_code ON geek_crm.products (tenant_id, code);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pricing_rules_tenant ON geek_crm.pricing_rules (tenant_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_holdings_tenant_line_item ON geek_crm.customer_holdings (tenant_id, deal_line_item_id);

CREATE INDEX IF NOT EXISTS ix_product_prices_product ON geek_crm.product_prices (tenant_id, product_id, effective_from);
CREATE INDEX IF NOT EXISTS ix_deal_line_items_deal ON geek_crm.deal_line_items (tenant_id, deal_id);
CREATE INDEX IF NOT EXISTS ix_customer_holdings_participant ON geek_crm.customer_holdings (tenant_id, participant_id);
CREATE INDEX IF NOT EXISTS ix_customer_holdings_product ON geek_crm.customer_holdings (tenant_id, product_id);

ALTER TABLE geek_crm.products DROP CONSTRAINT IF EXISTS fk_products_partner_participant_id_tenant;
ALTER TABLE geek_crm.products ADD CONSTRAINT fk_products_partner_participant_id_tenant
    FOREIGN KEY (tenant_id, partner_participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE SET NULL (partner_participant_id);

ALTER TABLE geek_crm.product_prices DROP CONSTRAINT IF EXISTS fk_product_prices_product_id_tenant;
ALTER TABLE geek_crm.product_prices ADD CONSTRAINT fk_product_prices_product_id_tenant
    FOREIGN KEY (tenant_id, product_id) REFERENCES geek_crm.products (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.deal_line_items DROP CONSTRAINT IF EXISTS fk_deal_line_items_deal_id_tenant;
ALTER TABLE geek_crm.deal_line_items ADD CONSTRAINT fk_deal_line_items_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.deal_line_items DROP CONSTRAINT IF EXISTS fk_deal_line_items_product_id_tenant;
ALTER TABLE geek_crm.deal_line_items ADD CONSTRAINT fk_deal_line_items_product_id_tenant
    FOREIGN KEY (tenant_id, product_id) REFERENCES geek_crm.products (tenant_id, id);

ALTER TABLE geek_crm.customer_holdings DROP CONSTRAINT IF EXISTS fk_customer_holdings_participant_id_tenant;
ALTER TABLE geek_crm.customer_holdings ADD CONSTRAINT fk_customer_holdings_participant_id_tenant
    FOREIGN KEY (tenant_id, participant_id) REFERENCES geek_crm.participants (tenant_id, id);

ALTER TABLE geek_crm.customer_holdings DROP CONSTRAINT IF EXISTS fk_customer_holdings_product_id_tenant;
ALTER TABLE geek_crm.customer_holdings ADD CONSTRAINT fk_customer_holdings_product_id_tenant
    FOREIGN KEY (tenant_id, product_id) REFERENCES geek_crm.products (tenant_id, id);

ALTER TABLE geek_crm.customer_holdings DROP CONSTRAINT IF EXISTS fk_customer_holdings_deal_id_tenant;
ALTER TABLE geek_crm.customer_holdings ADD CONSTRAINT fk_customer_holdings_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE SET NULL (deal_id);

ALTER TABLE geek_crm.customer_holdings DROP CONSTRAINT IF EXISTS fk_customer_holdings_deal_line_item_id_tenant;
ALTER TABLE geek_crm.customer_holdings ADD CONSTRAINT fk_customer_holdings_deal_line_item_id_tenant
    FOREIGN KEY (tenant_id, deal_line_item_id) REFERENCES geek_crm.deal_line_items (tenant_id, id) ON DELETE SET NULL (deal_line_item_id);

ALTER TABLE geek_crm.products ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.products FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.products;
CREATE POLICY tenant_isolation ON geek_crm.products
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.product_prices ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.product_prices FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.product_prices;
CREATE POLICY tenant_isolation ON geek_crm.product_prices
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.pricing_rules ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.pricing_rules FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.pricing_rules;
CREATE POLICY tenant_isolation ON geek_crm.pricing_rules
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.pricing_uplift_tiers ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.pricing_uplift_tiers FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.pricing_uplift_tiers;
CREATE POLICY tenant_isolation ON geek_crm.pricing_uplift_tiers
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.deal_line_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.deal_line_items FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.deal_line_items;
CREATE POLICY tenant_isolation ON geek_crm.deal_line_items
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.customer_holdings ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.customer_holdings FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.customer_holdings;
CREATE POLICY tenant_isolation ON geek_crm.customer_holdings
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
