-- 0053: the tenant boundary, enforced by the database (docs/GEEK-CRM-DATA-MODEL.md, "Tenant boundary").
--
-- Two layers, so a query that forgets its tenant filter still cannot cross tenants:
--
--   1. Every tenant-owned table gets UNIQUE (tenant_id, id), and every reference between tenant-owned
--      rows becomes a composite foreign key (tenant_id, x_id) -> (tenant_id, id). A row cannot point at
--      another tenant's participant, deal, stage or property, whatever the query says. The old
--      single-column keys (Postgres's default names, <table>_<column>_fkey) are replaced, with the
--      same ON DELETE behaviour. A nullable reference stays optional: a composite key with a NULL
--      column is not checked. ON DELETE SET NULL names only the reference column, so the row's
--      tenant_id is never nulled; that form needs PostgreSQL 15 or newer, and the script refuses to
--      run on an older server instead of guessing.
--
--   2. Row-level security on every geek_crm table that holds tenant data, FORCEd so the table's owner
--      is held to it too. GeekRepository runs set_config('app.tenant_id', <tenant>, true) (SET LOCAL)
--      at the start of each transaction (CrmRepository.RunAsync); with no tenant set, a read returns
--      nothing and a write is refused. persons and organizations carry no tenant_id of their own, so
--      their policy asks whether their participant is visible. The two industry lists are public data
--      and have no policy.
--
--      Row-level security is skipped for a superuser and for a role with BYPASSRLS. Before relying on
--      it, check GeekRepository's role:
--        select rolsuper, rolbypassrls from pg_roles where rolname = current_user;
--      and if either is true, connect as a dedicated role with rights only on the schemas it owns.
--
-- Idempotent: unique indexes are IF NOT EXISTS, constraints and policies are dropped before they are
-- added. Declared in GeekApplication/Schemas/GeekCrm.xsd (the composite keys and keyrefs).

DO $$
BEGIN
    IF current_setting('server_version_num')::int < 150000 THEN
        RAISE EXCEPTION 'geek_crm tenant keys need PostgreSQL 15 or newer (ON DELETE SET NULL with a column list); this server is %', current_setting('server_version');
    END IF;
END
$$;

-- ----- 1. Composite keys -----

CREATE UNIQUE INDEX IF NOT EXISTS ux_participants_tenant_id ON geek_crm.participants (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_contact_points_tenant_id ON geek_crm.contact_points (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_participant_links_tenant_id ON geek_crm.participant_links (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pipelines_tenant_id ON geek_crm.pipelines (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_stages_tenant_id ON geek_crm.stages (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_deals_tenant_id ON geek_crm.deals (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_deal_stage_history_tenant_id ON geek_crm.deal_stage_history (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_deal_participants_tenant_id ON geek_crm.deal_participants (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_activities_tenant_id ON geek_crm.activities (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_property_definitions_tenant_id ON geek_crm.property_definitions (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_property_values_tenant_id ON geek_crm.property_values (tenant_id, id);

ALTER TABLE geek_crm.contact_points DROP CONSTRAINT IF EXISTS contact_points_participant_id_fkey;
ALTER TABLE geek_crm.contact_points DROP CONSTRAINT IF EXISTS fk_contact_points_participant_id_tenant;
ALTER TABLE geek_crm.contact_points ADD CONSTRAINT fk_contact_points_participant_id_tenant
    FOREIGN KEY (tenant_id, participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.participant_links DROP CONSTRAINT IF EXISTS participant_links_from_participant_id_fkey;
ALTER TABLE geek_crm.participant_links DROP CONSTRAINT IF EXISTS fk_participant_links_from_participant_id_tenant;
ALTER TABLE geek_crm.participant_links ADD CONSTRAINT fk_participant_links_from_participant_id_tenant
    FOREIGN KEY (tenant_id, from_participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.participant_links DROP CONSTRAINT IF EXISTS participant_links_to_participant_id_fkey;
ALTER TABLE geek_crm.participant_links DROP CONSTRAINT IF EXISTS fk_participant_links_to_participant_id_tenant;
ALTER TABLE geek_crm.participant_links ADD CONSTRAINT fk_participant_links_to_participant_id_tenant
    FOREIGN KEY (tenant_id, to_participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.stages DROP CONSTRAINT IF EXISTS stages_pipeline_id_fkey;
ALTER TABLE geek_crm.stages DROP CONSTRAINT IF EXISTS fk_stages_pipeline_id_tenant;
ALTER TABLE geek_crm.stages ADD CONSTRAINT fk_stages_pipeline_id_tenant
    FOREIGN KEY (tenant_id, pipeline_id) REFERENCES geek_crm.pipelines (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS deals_pipeline_id_fkey;
ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS fk_deals_pipeline_id_tenant;
ALTER TABLE geek_crm.deals ADD CONSTRAINT fk_deals_pipeline_id_tenant
    FOREIGN KEY (tenant_id, pipeline_id) REFERENCES geek_crm.pipelines (tenant_id, id);

ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS deals_stage_id_fkey;
ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS fk_deals_stage_id_tenant;
ALTER TABLE geek_crm.deals ADD CONSTRAINT fk_deals_stage_id_tenant
    FOREIGN KEY (tenant_id, stage_id) REFERENCES geek_crm.stages (tenant_id, id);

ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS deals_organization_participant_id_fkey;
ALTER TABLE geek_crm.deals DROP CONSTRAINT IF EXISTS fk_deals_organization_participant_id_tenant;
ALTER TABLE geek_crm.deals ADD CONSTRAINT fk_deals_organization_participant_id_tenant
    FOREIGN KEY (tenant_id, organization_participant_id) REFERENCES geek_crm.participants (tenant_id, id);

ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS deal_stage_history_deal_id_fkey;
ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS fk_deal_stage_history_deal_id_tenant;
ALTER TABLE geek_crm.deal_stage_history ADD CONSTRAINT fk_deal_stage_history_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS deal_stage_history_from_stage_id_fkey;
ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS fk_deal_stage_history_from_stage_id_tenant;
ALTER TABLE geek_crm.deal_stage_history ADD CONSTRAINT fk_deal_stage_history_from_stage_id_tenant
    FOREIGN KEY (tenant_id, from_stage_id) REFERENCES geek_crm.stages (tenant_id, id);

ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS deal_stage_history_to_stage_id_fkey;
ALTER TABLE geek_crm.deal_stage_history DROP CONSTRAINT IF EXISTS fk_deal_stage_history_to_stage_id_tenant;
ALTER TABLE geek_crm.deal_stage_history ADD CONSTRAINT fk_deal_stage_history_to_stage_id_tenant
    FOREIGN KEY (tenant_id, to_stage_id) REFERENCES geek_crm.stages (tenant_id, id);

ALTER TABLE geek_crm.deal_participants DROP CONSTRAINT IF EXISTS deal_participants_deal_id_fkey;
ALTER TABLE geek_crm.deal_participants DROP CONSTRAINT IF EXISTS fk_deal_participants_deal_id_tenant;
ALTER TABLE geek_crm.deal_participants ADD CONSTRAINT fk_deal_participants_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.deal_participants DROP CONSTRAINT IF EXISTS deal_participants_participant_id_fkey;
ALTER TABLE geek_crm.deal_participants DROP CONSTRAINT IF EXISTS fk_deal_participants_participant_id_tenant;
ALTER TABLE geek_crm.deal_participants ADD CONSTRAINT fk_deal_participants_participant_id_tenant
    FOREIGN KEY (tenant_id, participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE CASCADE;

ALTER TABLE geek_crm.activities DROP CONSTRAINT IF EXISTS activities_participant_id_fkey;
ALTER TABLE geek_crm.activities DROP CONSTRAINT IF EXISTS fk_activities_participant_id_tenant;
ALTER TABLE geek_crm.activities ADD CONSTRAINT fk_activities_participant_id_tenant
    FOREIGN KEY (tenant_id, participant_id) REFERENCES geek_crm.participants (tenant_id, id) ON DELETE SET NULL (participant_id);

ALTER TABLE geek_crm.activities DROP CONSTRAINT IF EXISTS activities_deal_id_fkey;
ALTER TABLE geek_crm.activities DROP CONSTRAINT IF EXISTS fk_activities_deal_id_tenant;
ALTER TABLE geek_crm.activities ADD CONSTRAINT fk_activities_deal_id_tenant
    FOREIGN KEY (tenant_id, deal_id) REFERENCES geek_crm.deals (tenant_id, id) ON DELETE SET NULL (deal_id);

ALTER TABLE geek_crm.property_values DROP CONSTRAINT IF EXISTS property_values_definition_id_fkey;
ALTER TABLE geek_crm.property_values DROP CONSTRAINT IF EXISTS fk_property_values_definition_id_tenant;
ALTER TABLE geek_crm.property_values ADD CONSTRAINT fk_property_values_definition_id_tenant
    FOREIGN KEY (tenant_id, definition_id) REFERENCES geek_crm.property_definitions (tenant_id, id) ON DELETE CASCADE;

-- ----- 2. Row-level security -----

ALTER TABLE geek_crm.participants ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.participants FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.participants;
CREATE POLICY tenant_isolation ON geek_crm.participants
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.contact_points ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.contact_points FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.contact_points;
CREATE POLICY tenant_isolation ON geek_crm.contact_points
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.participant_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.participant_links FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.participant_links;
CREATE POLICY tenant_isolation ON geek_crm.participant_links
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.pipelines ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.pipelines FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.pipelines;
CREATE POLICY tenant_isolation ON geek_crm.pipelines
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.stages ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.stages FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.stages;
CREATE POLICY tenant_isolation ON geek_crm.stages
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.deals ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.deals FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.deals;
CREATE POLICY tenant_isolation ON geek_crm.deals
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.deal_stage_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.deal_stage_history FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.deal_stage_history;
CREATE POLICY tenant_isolation ON geek_crm.deal_stage_history
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.deal_participants ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.deal_participants FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.deal_participants;
CREATE POLICY tenant_isolation ON geek_crm.deal_participants
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.activities ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.activities FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.activities;
CREATE POLICY tenant_isolation ON geek_crm.activities
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.property_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.property_definitions FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.property_definitions;
CREATE POLICY tenant_isolation ON geek_crm.property_definitions
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.property_values ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.property_values FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.property_values;
CREATE POLICY tenant_isolation ON geek_crm.property_values
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.tenants FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.tenants;
CREATE POLICY tenant_isolation ON geek_crm.tenants
    USING (id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE geek_crm.persons ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.persons FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.persons;
CREATE POLICY tenant_isolation ON geek_crm.persons
    USING (EXISTS (SELECT 1 FROM geek_crm.participants p WHERE p.id = participant_id))
    WITH CHECK (EXISTS (SELECT 1 FROM geek_crm.participants p WHERE p.id = participant_id));

ALTER TABLE geek_crm.organizations ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.organizations FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.organizations;
CREATE POLICY tenant_isolation ON geek_crm.organizations
    USING (EXISTS (SELECT 1 FROM geek_crm.participants p WHERE p.id = participant_id))
    WITH CHECK (EXISTS (SELECT 1 FROM geek_crm.participants p WHERE p.id = participant_id));
