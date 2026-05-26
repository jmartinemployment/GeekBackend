-- Greenfield cleanup: remove OpenIddict issuer tables, OAuth adapter storage, and JWT replay list.
-- Does not drop geek_seo (see EF migrations in GeekRepository/Migrations/Seo).
-- Safe to re-run (IF EXISTS). Does not drop devices_oauth (device management, not OAuth protocol).

-- OpenIddict (issuer on GeekAPI era)
DROP TABLE IF EXISTS openiddict_tokens CASCADE;
DROP TABLE IF EXISTS openiddict_authorizations CASCADE;
DROP TABLE IF EXISTS openiddict_scopes CASCADE;
DROP TABLE IF EXISTS openiddict_applications CASCADE;

-- Access-token replay detection (issuer era)
DROP TABLE IF EXISTS public.jti_blacklist CASCADE;

-- OIDC adapter / interim storage (public snake_case and geek_auth PascalCase)
DROP TABLE IF EXISTS public.oidc_storage CASCADE;
DROP TABLE IF EXISTS geek_auth."OidcStorage" CASCADE;
DROP TABLE IF EXISTS geek_auth."OAuthToken" CASCADE;
DROP TABLE IF EXISTS geek_auth."OAuthClient" CASCADE;

-- auth.oauth_clients / auth.sessions are owned by Supabase Auth — do not drop here
-- (requires service_role / dashboard SQL if you need to remove them).

-- geek_seo schema: owned by GeekRepository EF migrations (GeekApplication/Seo). Do not drop here.
