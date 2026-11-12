-- Geek-CRM: the first owner of tenant #1 (Geek @ Your Spot), so someone can sign in before the platform routes have been used.
-- user_id is the GeekOAuth `sub` of Jeff's account. Idempotent: a user already in tenant_users is left as is, so a later
-- change of role or removal through /api/platform/tenant-users is not undone when this script is read again.
-- tenant_users is readable and writable only with app.platform = 'on' (or app.tenant_id of its own tenant); the setting is
-- transaction-local, and the migration runner runs each script in one transaction.
SELECT set_config('app.platform', 'on', true);

INSERT INTO geek_crm.tenant_users (id, tenant_id, user_id, role, created_at)
VALUES (gen_random_uuid(), '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '92b274f5-2fcb-4935-ba2d-cd8c03e1b21b', 'owner', now())
ON CONFLICT (user_id) DO NOTHING;
