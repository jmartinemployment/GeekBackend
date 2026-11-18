-- GeekSocial, roadmap Phase 0 item 2: auto-send is removed. A person approves every reply before it is sent
-- (docs/PLATFORM-CAPABILITIES.md section 1a). GeekAPI stopped auto-sending in 02de4a82, so nothing has read the
-- setting since; the column and its check go with the code, and no setting can bring automatic sending back.

ALTER TABLE geek_social.social_accounts DROP CONSTRAINT IF EXISTS ck_social_accounts_auto_send;
ALTER TABLE geek_social.social_accounts DROP COLUMN IF EXISTS auto_send_categories;
