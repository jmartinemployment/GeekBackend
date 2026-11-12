-- Geek-CRM: the role every tenant-scoped operation runs as, so row-level security applies whatever login GeekRepository
-- connects with. A superuser, or a login with BYPASSRLS (Supabase's default `postgres` has it), skips row-level security
-- even on a FORCEd table; a role that is neither cannot. PostgresCrmTenantScope runs `SET LOCAL ROLE geek_crm_app` at the start
-- of every tenant transaction, which drops the login's privileges for that transaction and puts the policies in force.
-- NOLOGIN: nothing connects as this role, and it needs no password. The connecting login is made a member so it may SET ROLE to it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'geek_crm_app') THEN
        CREATE ROLE geek_crm_app NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOINHERIT;
    END IF;
END
$$;

GRANT geek_crm_app TO CURRENT_USER;

GRANT USAGE ON SCHEMA geek_crm TO geek_crm_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA geek_crm TO geek_crm_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA geek_crm TO geek_crm_app;

-- Tables a later script creates are reachable too, so a new table is never silently unreadable to the role.
ALTER DEFAULT PRIVILEGES IN SCHEMA geek_crm GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO geek_crm_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA geek_crm GRANT USAGE, SELECT ON SEQUENCES TO geek_crm_app;
