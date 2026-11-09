-- 0043: the GeekSocial store, geek_social, in GeekRepository's own database.
--
-- GeekSocial publishes to Facebook, Instagram and LinkedIn, watches the comments and reactions that
-- come back, and sends replies after a person approves them. GeekAPI reaches this schema only
-- through repo/social/*; nothing else connects to it (AGENTS.md, "Nothing reaches Postgres except
-- GeekRepository"). A product store is a schema in this database, not a second Mongo.
--
-- Raw SQL rather than an EF migration, matching geek_gtm, geek_visitors and geek_blog: the schema
-- is created and maintained by Migrations/Sql/*.sql through SqlMigrationRunner, and SocialDbContext
-- has no EF migrations and no MigrationsHistoryTable. SocialDbContext maps every column named here
-- by the snake_case of its property name, so the two are read against each other, and the
-- GeekBackend.IntegrationTests schema test applies this script to an empty database and queries
-- every set of the context, so a column one of them lacks fails the build.
--
-- The CHECK constraints repeat the vocabularies in GeekApplication/Models/Social. A value the code
-- does not know is refused by the database too, and GeekBackend.Tests compares the lists in this
-- file with the code's, so the two cannot drift apart unnoticed.
--
-- Tokens: token_cipher, token_iv and token_tag are AES-256-GCM output that GeekAPI produced. The
-- key lives only in GeekAPI (GEEK_SOCIAL_ENCRYPTION_KEY); this database never holds it.
--
-- Retention: LinkedIn lets another member's comment text be kept 48 hours and a commenter's profile
-- data 24 hours. text_expires_at says when an event's text must be gone and author_expires_at when
-- the commenter's name must be, and repo/social/maintenance/purge scrubs both. A read never returns
-- either past its deadline, purged or not. Postgres has no TTL index, so GeekAPI calls the purge.

CREATE SCHEMA IF NOT EXISTS geek_social;

-- One connected Page, Instagram account, LinkedIn organization or LinkedIn member.
CREATE TABLE IF NOT EXISTS geek_social.social_accounts (
    id                   uuid PRIMARY KEY,
    platform             text NOT NULL,
    platform_account_id  text NOT NULL,
    display_name         text NOT NULL,
    status               text NOT NULL DEFAULT 'connected',
    auto_send_categories text[] NOT NULL DEFAULT '{}',
    sends_disabled       boolean NOT NULL DEFAULT false,
    token_cipher         bytea NOT NULL,
    token_iv             bytea NOT NULL,
    token_tag            bytea NOT NULL,
    token_expires_at     timestamptz NULL,
    connected_at         timestamptz NOT NULL,
    version              integer NOT NULL DEFAULT 0,
    CONSTRAINT ck_social_accounts_platform
        CHECK (platform IN ('facebook', 'instagram', 'linkedin_organization', 'linkedin_member')),
    CONSTRAINT ck_social_accounts_status
        CHECK (status IN ('connected', 'reconnect_required')),
    -- Complaint and sensitive are never sent without a person; no setting can list them.
    CONSTRAINT ck_social_accounts_auto_send
        CHECK (auto_send_categories <@ ARRAY['question', 'praise', 'spam']::text[])
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_social_accounts_platform_account
    ON geek_social.social_accounts (platform, platform_account_id);

-- A post the agent published.
CREATE TABLE IF NOT EXISTS geek_social.social_posts (
    id               uuid PRIMARY KEY,
    account_id       uuid NOT NULL REFERENCES geek_social.social_accounts (id),
    platform         text NOT NULL,
    platform_post_id text NOT NULL,
    kind             text NOT NULL,
    text             text NOT NULL DEFAULT '',
    link_url         text NULL,
    scheduled_for    timestamptz NULL,
    published_at     timestamptz NULL,
    created_at       timestamptz NOT NULL,
    CONSTRAINT ck_social_posts_kind CHECK (kind IN ('link', 'image'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_social_posts_account_post
    ON geek_social.social_posts (account_id, platform_post_id);

CREATE INDEX IF NOT EXISTS ix_social_posts_account_created
    ON geek_social.social_posts (account_id, created_at DESC);

-- One thing that happened on a post. A platform redelivers, so (platform, platform_event_id) is
-- unique and the second delivery inserts nothing.
CREATE TABLE IF NOT EXISTS geek_social.engagement_events (
    id                         uuid PRIMARY KEY,
    account_id                 uuid NOT NULL REFERENCES geek_social.social_accounts (id),
    platform                   text NOT NULL,
    platform_event_id          text NOT NULL,
    kind                       text NOT NULL,
    platform_post_id           text NULL,
    platform_comment_id        text NULL,
    parent_platform_comment_id text NULL,
    author_platform_id         text NULL,
    author_display_name        text NULL,
    text                       text NULL,
    reaction_type              text NULL,
    occurred_at                timestamptz NOT NULL,
    received_at                timestamptz NOT NULL,
    text_expires_at            timestamptz NULL,
    author_expires_at          timestamptz NULL,
    CONSTRAINT ck_engagement_events_platform
        CHECK (platform IN ('facebook', 'instagram', 'linkedin_organization', 'linkedin_member')),
    CONSTRAINT ck_engagement_events_kind
        CHECK (kind IN ('comment_created', 'comment_edited', 'comment_deleted', 'comment_hidden',
                        'comment_unhidden', 'reaction_added', 'reaction_removed', 'share', 'mention'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_engagement_events_platform_event
    ON geek_social.engagement_events (platform, platform_event_id);

CREATE INDEX IF NOT EXISTS ix_engagement_events_account_received
    ON geek_social.engagement_events (account_id, received_at DESC);

-- The purge reads only the rows that still hold text or a name with a deadline.
CREATE INDEX IF NOT EXISTS ix_engagement_events_text_expires
    ON geek_social.engagement_events (text_expires_at)
    WHERE text IS NOT NULL AND text_expires_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_engagement_events_author_expires
    ON geek_social.engagement_events (author_expires_at)
    WHERE author_display_name IS NOT NULL AND author_expires_at IS NOT NULL;

-- A reply Claude drafted for one event, waiting for a person.
CREATE TABLE IF NOT EXISTS geek_social.reply_drafts (
    id                       uuid PRIMARY KEY,
    account_id               uuid NOT NULL REFERENCES geek_social.social_accounts (id),
    engagement_event_id      uuid NOT NULL REFERENCES geek_social.engagement_events (id),
    platform                 text NOT NULL,
    category                 text NOT NULL,
    draft_text               text NOT NULL,
    needs_human_reason       text NULL,
    status                   text NOT NULL DEFAULT 'pending',
    sent_platform_comment_id text NULL,
    failure_code             text NULL,
    created_at               timestamptz NOT NULL,
    decided_at               timestamptz NULL,
    sending_started_at       timestamptz NULL,
    CONSTRAINT ck_reply_drafts_platform
        CHECK (platform IN ('facebook', 'instagram', 'linkedin_organization', 'linkedin_member')),
    CONSTRAINT ck_reply_drafts_category
        CHECK (category IN ('question', 'praise', 'complaint', 'spam', 'sensitive')),
    CONSTRAINT ck_reply_drafts_status
        CHECK (status IN ('pending', 'approved', 'rejected', 'sending', 'sent', 'failed')),
    CONSTRAINT ck_reply_drafts_sent_has_id
        CHECK (status <> 'sent' OR sent_platform_comment_id IS NOT NULL),
    CONSTRAINT ck_reply_drafts_failed_has_code
        CHECK (status <> 'failed' OR failure_code IS NOT NULL)
);

-- A comment is never answered twice. A live draft, a sent reply, or a send whose outcome is
-- unknown (the platform may have posted it) each hold the comment; a rejected draft, or a failure
-- where nothing was applied, does not, so a new draft can be made.
CREATE UNIQUE INDEX IF NOT EXISTS ux_reply_drafts_one_answer_per_event
    ON geek_social.reply_drafts (engagement_event_id)
    WHERE status IN ('pending', 'approved', 'sending', 'sent') OR failure_code = 'outcome_unknown';

CREATE INDEX IF NOT EXISTS ix_reply_drafts_status_created
    ON geek_social.reply_drafts (status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_reply_drafts_account_created
    ON geek_social.reply_drafts (account_id, created_at DESC);

-- One row for every call the agent made to a platform that changed something there, or tried to.
CREATE TABLE IF NOT EXISTS geek_social.action_audit (
    id                 uuid PRIMARY KEY,
    account_id         uuid NULL REFERENCES geek_social.social_accounts (id),
    platform           text NOT NULL,
    action             text NOT NULL,
    outcome            text NOT NULL,
    platform_object_id text NULL,
    reply_draft_id     uuid NULL REFERENCES geek_social.reply_drafts (id),
    failure_code       text NULL,
    failure_detail     text NULL,
    actor_user_id      text NULL,
    occurred_at        timestamptz NOT NULL,
    CONSTRAINT ck_action_audit_platform
        CHECK (platform IN ('facebook', 'instagram', 'linkedin_organization', 'linkedin_member')),
    CONSTRAINT ck_action_audit_action
        CHECK (action IN ('connect', 'refresh_token', 'subscribe_webhook', 'publish', 'reply', 'react')),
    CONSTRAINT ck_action_audit_outcome
        CHECK (outcome IN ('succeeded', 'failed', 'outcome_unknown')),
    CONSTRAINT ck_action_audit_failure_has_code
        CHECK (outcome = 'succeeded' OR failure_code IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_action_audit_occurred
    ON geek_social.action_audit (occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_action_audit_account_occurred
    ON geek_social.action_audit (account_id, occurred_at DESC);
