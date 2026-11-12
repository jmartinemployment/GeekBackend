-- 0057: Geek-CRM Phase 4, tenant membership (docs/GEEK-CRM-DATA-MODEL.md 4.17).
--
-- tenant_users maps a GeekOAuth user (user_id = the JWT sub) to the one tenant they work in, with a role
-- (owner, member). The tenant of a signed-in request comes from here and nowhere else. A user belongs to one
-- tenant (unique user_id). Like every tenant table it has UNIQUE (tenant_id, id), a composite-ready key and
-- forced row-level security on tenant_id; a second policy, platform_access, lets a platform operation (the
-- membership lookup, which does not yet know the tenant, and the platform-admin routes that add and remove
-- members) read and write across tenants. Declared in GeekApplication/Schemas/GeekCrm.xsd. Idempotent.

CREATE TABLE IF NOT EXISTS geek_crm.tenant_users (
    id           uuid PRIMARY KEY,
    tenant_id    uuid NOT NULL REFERENCES geek_crm.tenants (id),
    user_id      uuid NOT NULL,
    role         text NOT NULL CONSTRAINT ck_tenant_users_role CHECK (role IN ('owner', 'member')),
    created_at   timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_users_tenant_id ON geek_crm.tenant_users (tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_users_user ON geek_crm.tenant_users (user_id);

ALTER TABLE geek_crm.tenant_users ENABLE ROW LEVEL SECURITY;
ALTER TABLE geek_crm.tenant_users FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON geek_crm.tenant_users;
CREATE POLICY tenant_isolation ON geek_crm.tenant_users
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
DROP POLICY IF EXISTS platform_access ON geek_crm.tenant_users;
CREATE POLICY platform_access ON geek_crm.tenant_users
    USING (nullif(current_setting('app.platform', true), '') = 'on')
    WITH CHECK (nullif(current_setting('app.platform', true), '') = 'on');
