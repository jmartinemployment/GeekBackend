-- 0064: GeekSocial Gate 7b, tenants (plans/geek-social-7b-tenants-2026-11-16.md).
--
-- geek_social.tenants is one tenant per GeekOAuth user (owner_user_id is the JWT sub). Every row of the five
-- social tables belongs to exactly one tenant through tenant_id. ai_consent_accepted_at is when the tenant
-- accepted the AI terms; until it is set, none of the tenant's content may reach the AI.
--
-- Isolation is enforced in GeekRepository: every query filters on tenant_id and every ISocialStore method
-- takes the tenant, so a row of another tenant answers not found. There is deliberately no row-level security
-- here: this repository writes through EF Core and reads through Dapper on separate connections, so a
-- per-transaction tenant setting would not reach both, and a policy that nothing sets would be a safety
-- property asserted but not enforced. Row-level security needs the repository's connections reworked first.

-- A platform account and a platform event keep their global unique indexes: one Page, or one comment, belongs
-- to one tenant, and the webhook intake finds the tenant from it.
--
-- Production has no rows. If a database does hold any, the script stops here and says so rather than guessing an
-- owner: a row without a tenant is exactly the leak this script exists to close. Idempotent.

CREATE TABLE IF NOT EXISTS geek_social.tenants (
    id                     uuid PRIMARY KEY,
    owner_user_id          uuid NOT NULL,
    name                   text NOT NULL,
    ai_consent_accepted_at timestamptz NULL,
    created_at             timestamptz NOT NULL,
    CONSTRAINT ck_social_tenants_name CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_social_tenants_owner ON geek_social.tenants (owner_user_id);

DO $$
DECLARE
    t text;
    n bigint;
BEGIN
    FOREACH t IN ARRAY ARRAY['social_accounts', 'social_posts', 'engagement_events', 'reply_drafts', 'action_audit']
    LOOP
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'geek_social' AND table_name = t AND column_name = 'tenant_id') THEN
            EXECUTE format('SELECT count(*) FROM geek_social.%I', t) INTO n;
            IF n > 0 THEN
                RAISE EXCEPTION 'geek_social.% holds % row(s) with no tenant. Delete them or assign an owner before applying 0064.', t, n;
            END IF;
        END IF;
    END LOOP;
END
$$;

ALTER TABLE geek_social.social_accounts    ADD COLUMN IF NOT EXISTS tenant_id uuid NOT NULL REFERENCES geek_social.tenants (id);
ALTER TABLE geek_social.social_posts       ADD COLUMN IF NOT EXISTS tenant_id uuid NOT NULL REFERENCES geek_social.tenants (id);
ALTER TABLE geek_social.engagement_events  ADD COLUMN IF NOT EXISTS tenant_id uuid NOT NULL REFERENCES geek_social.tenants (id);
ALTER TABLE geek_social.reply_drafts       ADD COLUMN IF NOT EXISTS tenant_id uuid NOT NULL REFERENCES geek_social.tenants (id);
ALTER TABLE geek_social.action_audit       ADD COLUMN IF NOT EXISTS tenant_id uuid NOT NULL REFERENCES geek_social.tenants (id);

CREATE INDEX IF NOT EXISTS ix_social_accounts_tenant ON geek_social.social_accounts (tenant_id);
CREATE INDEX IF NOT EXISTS ix_social_posts_tenant_created ON geek_social.social_posts (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_engagement_events_tenant_received ON geek_social.engagement_events (tenant_id, received_at DESC);
CREATE INDEX IF NOT EXISTS ix_reply_drafts_tenant_created ON geek_social.reply_drafts (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_action_audit_tenant_occurred ON geek_social.action_audit (tenant_id, occurred_at DESC);
