-- Run once on Supabase instance (manual — not applied by EF migrations).
-- Creates dedicated principal for Geek SEO schema only.

CREATE ROLE geekseo_app LOGIN PASSWORD 'REPLACE_WITH_STRONG_PASSWORD';

CREATE SCHEMA IF NOT EXISTS geek_seo;

GRANT USAGE ON SCHEMA geek_seo TO geekseo_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA geek_seo TO geekseo_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA geek_seo TO geekseo_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA geek_seo
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO geekseo_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA geek_seo
  GRANT USAGE, SELECT ON SEQUENCES TO geekseo_app;

-- Do NOT grant geekseo_app access to auth, public content, or other schemas.
