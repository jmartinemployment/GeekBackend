-- Run once on Supabase instance (manual — not applied by EF migrations).
-- Creates dedicated principal for Geek SEO schema only.
-- EF migrations run as DATABASE_URL (admin); set GEEK_SEO_DATABASE_URL to geekseo_app at runtime.

CREATE ROLE geekseo_app LOGIN PASSWORD 'REPLACE_WITH_STRONG_PASSWORD';

CREATE SCHEMA IF NOT EXISTS geek_seo;

GRANT USAGE ON SCHEMA geek_seo TO geekseo_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA geek_seo TO geekseo_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA geek_seo TO geekseo_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA geek_seo
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO geekseo_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA geek_seo
  GRANT USAGE, SELECT ON SEQUENCES TO geekseo_app;

-- EF history (read-only for runtime role; migrations use admin DATABASE_URL)
GRANT SELECT ON TABLE geek_seo."__EFSeoMigrationsHistory" TO geekseo_app;

-- Do NOT grant geekseo_app access to auth, public content, or other schemas.
