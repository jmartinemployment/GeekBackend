-- O2: Drop legacy Geek platform auth tables (Dapper). Identity lives in GeekOAuth (separate DB / asp_net_users).
-- Does NOT drop: departments, case_studies, use_cases, geek_seo schema, schema_migrations, auth.oauth_* (Supabase).
-- Safe to re-run (IF EXISTS). CASCADE clears FKs among these tables only.

DROP TABLE IF EXISTS public.websocket_sessions CASCADE;

DROP TABLE IF EXISTS public.two_factor_trusted_devices CASCADE;
DROP TABLE IF EXISTS public.two_factor_pending_sessions CASCADE;
DROP TABLE IF EXISTS public.device_registration_codes CASCADE;
DROP TABLE IF EXISTS public.pending_verifications CASCADE;
DROP TABLE IF EXISTS public.device_reregistration_requests CASCADE;
DROP TABLE IF EXISTS public.devices_oauth CASCADE;
DROP TABLE IF EXISTS public.sync_conflicts CASCADE;
DROP TABLE IF EXISTS public.sync_queue CASCADE;
DROP TABLE IF EXISTS public.user_claims CASCADE;
DROP TABLE IF EXISTS public.user_secrets CASCADE;
DROP TABLE IF EXISTS public.audit_log CASCADE;
DROP TABLE IF EXISTS public.security_incidents CASCADE;

DROP TABLE IF EXISTS public.role_permissions CASCADE;
DROP TABLE IF EXISTS public.user_roles CASCADE;
DROP TABLE IF EXISTS public.permissions CASCADE;
DROP TABLE IF EXISTS public.roles CASCADE;

DROP TABLE IF EXISTS public.users CASCADE;
