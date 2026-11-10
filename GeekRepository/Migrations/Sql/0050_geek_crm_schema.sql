-- 0050: Geek-CRM, geek_crm, in GeekRepository's own database (docs/GEEK-CRM-DATA-MODEL.md).
--
-- Numbered 0050, not 0044, to leave room for GeekSocial scripts in flight on other branches; the
-- runner applies scripts in file-name order and records each by name, so a gap is harmless.
--
-- Raw SQL rather than an EF migration, the same as geek_social, geek_visitors and geek_gtm.
-- CrmDbContext maps every column by the snake_case of its property name. The CHECK constraints
-- repeat the vocabularies in GeekApplication/Models/Crm/CrmVocabulary.cs, and GeekBackend.Tests
-- compares the two lists.
--
-- Multi-tenant from day one: every table carries tenant_id and every query is scoped by it.
-- Geek @ Your Spot is tenant #1 (seeded below, id = CrmSeed.GeekAtYourSpotTenantId).
--
-- Designed independently from Geek @ Your Spot's requirements, common CRM concepts and the
-- general party pattern; no ACORD names, code lists or structure.

CREATE SCHEMA IF NOT EXISTS geek_crm;

CREATE TABLE IF NOT EXISTS geek_crm.tenants (
    id          uuid PRIMARY KEY,
    name        text NOT NULL,
    slug        text NOT NULL UNIQUE,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Anyone we deal with: a person or an organization.
CREATE TABLE IF NOT EXISTS geek_crm.participants (
    id               uuid PRIMARY KEY,
    tenant_id        uuid NOT NULL REFERENCES geek_crm.tenants (id),
    kind             text NOT NULL,
    display_name     text NOT NULL,
    lifecycle_stage  text NOT NULL DEFAULT 'lead',
    source           text NOT NULL,
    source_detail    text NULL,
    owner_user_id    uuid NULL,
    preferred_language text NULL,
    notes            text NULL,
    external_id      text NULL,
    created_at       timestamptz NOT NULL,
    updated_at       timestamptz NOT NULL,
    archived_at      timestamptz NULL,
    CONSTRAINT ck_participants_kind CHECK (kind IN ('person', 'organization')),
    CONSTRAINT ck_participants_lifecycle_stage
        CHECK (lifecycle_stage IN ('lead', 'prospect', 'customer', 'former_customer', 'other')),
    CONSTRAINT ck_participants_source
        CHECK (source IN ('phone_call', 'text', 'website', 'referral', 'import', 'event', 'outreach', 'other'))
);
CREATE INDEX IF NOT EXISTS ix_participants_tenant_stage ON geek_crm.participants (tenant_id, lifecycle_stage);
CREATE UNIQUE INDEX IF NOT EXISTS ux_participants_tenant_external
    ON geek_crm.participants (tenant_id, kind, external_id) WHERE external_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS geek_crm.persons (
    participant_id   uuid PRIMARY KEY REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    first_name       text NULL,
    last_name        text NULL,
    preferred_name   text NULL,
    job_title        text NULL,
    -- Demographics: optional, recorded only when the person provides them.
    birth_date       date NULL,
    gender           text NULL,
    marital_status   text NULL,
    dependents_count integer NULL,
    education_level  text NULL,
    occupation       text NULL,
    CONSTRAINT ck_persons_marital_status
        CHECK (marital_status IS NULL OR marital_status IN ('single', 'married', 'domestic_partner', 'divorced', 'widowed')),
    CONSTRAINT ck_persons_education_level
        CHECK (education_level IS NULL OR education_level IN ('high_school', 'some_college', 'associate', 'bachelor', 'master', 'doctorate', 'other'))
);

CREATE TABLE IF NOT EXISTS geek_crm.organizations (
    participant_id      uuid PRIMARY KEY REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    legal_name          text NOT NULL,
    dba                 text NULL,
    organization_type   text NULL,
    naics_code          text NULL,
    sic_code            text NULL,
    industry_label      text NULL,
    employee_count      integer NULL,
    year_started        integer NULL,
    annual_revenue_band text NULL,
    website             text NULL,
    service_area        text NULL,
    description         text NULL,
    CONSTRAINT ck_organizations_type CHECK (organization_type IS NULL OR organization_type IN
        ('sole_proprietor', 'llc', 'partnership', 'c_corporation', 's_corporation', 'nonprofit', 'government', 'other')),
    CONSTRAINT ck_organizations_revenue_band CHECK (annual_revenue_band IS NULL OR annual_revenue_band IN
        ('under_250k', '250k_1m', '1m_5m', '5m_25m', 'over_25m'))
);
CREATE INDEX IF NOT EXISTS ix_organizations_naics ON geek_crm.organizations (naics_code);

-- Phones, emails, addresses, websites and social profiles in one table.
CREATE TABLE IF NOT EXISTS geek_crm.contact_points (
    id              uuid PRIMARY KEY,
    tenant_id       uuid NOT NULL REFERENCES geek_crm.tenants (id),
    participant_id  uuid NOT NULL REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    kind            text NOT NULL,
    label           text NOT NULL DEFAULT 'other',
    value           text NOT NULL,
    address_line1   text NULL,
    address_line2   text NULL,
    city            text NULL,
    region          text NULL,
    postal_code     text NULL,
    country         text NULL,
    is_primary      boolean NOT NULL DEFAULT false,
    do_not_contact  boolean NOT NULL DEFAULT false,
    sms_opt_out     boolean NOT NULL DEFAULT false,
    verified_at     timestamptz NULL,
    created_at      timestamptz NOT NULL,
    CONSTRAINT ck_contact_points_kind CHECK (kind IN ('phone', 'email', 'address', 'website', 'social')),
    CONSTRAINT ck_contact_points_label CHECK (label IN ('mobile', 'work', 'home', 'main', 'billing', 'other'))
);
CREATE INDEX IF NOT EXISTS ix_contact_points_lookup ON geek_crm.contact_points (tenant_id, kind, value);
CREATE INDEX IF NOT EXISTS ix_contact_points_participant ON geek_crm.contact_points (participant_id);

CREATE TABLE IF NOT EXISTS geek_crm.participant_links (
    id                   uuid PRIMARY KEY,
    tenant_id            uuid NOT NULL REFERENCES geek_crm.tenants (id),
    from_participant_id  uuid NOT NULL REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    to_participant_id    uuid NOT NULL REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    role                 text NOT NULL,
    is_primary           boolean NOT NULL DEFAULT false,
    started_on           date NULL,
    ended_on             date NULL,
    created_at           timestamptz NOT NULL,
    CONSTRAINT ck_participant_links_role CHECK (role IN
        ('owner', 'employee', 'decision_maker', 'billing_contact', 'referred_by', 'partner', 'other')),
    CONSTRAINT ck_participant_links_not_self CHECK (from_participant_id <> to_participant_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_participant_links_pair_role
    ON geek_crm.participant_links (from_participant_id, to_participant_id, role);

CREATE TABLE IF NOT EXISTS geek_crm.pipelines (
    id          uuid PRIMARY KEY,
    tenant_id   uuid NOT NULL REFERENCES geek_crm.tenants (id),
    name        text NOT NULL,
    is_default  boolean NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS geek_crm.stages (
    id               uuid PRIMARY KEY,
    tenant_id        uuid NOT NULL REFERENCES geek_crm.tenants (id),
    pipeline_id      uuid NOT NULL REFERENCES geek_crm.pipelines (id) ON DELETE CASCADE,
    name             text NOT NULL,
    position         integer NOT NULL,
    outcome          text NOT NULL DEFAULT 'open',
    probability_pct  integer NULL,
    CONSTRAINT ck_stages_outcome CHECK (outcome IN ('open', 'won', 'lost')),
    CONSTRAINT ux_stages_pipeline_name UNIQUE (pipeline_id, name)
);

CREATE TABLE IF NOT EXISTS geek_crm.deals (
    id                           uuid PRIMARY KEY,
    tenant_id                    uuid NOT NULL REFERENCES geek_crm.tenants (id),
    name                         text NOT NULL,
    pipeline_id                  uuid NOT NULL REFERENCES geek_crm.pipelines (id),
    stage_id                     uuid NOT NULL REFERENCES geek_crm.stages (id),
    organization_participant_id  uuid NULL REFERENCES geek_crm.participants (id),
    product_category             text NOT NULL,
    qualification                jsonb NULL,
    estimated_value              numeric(12, 2) NULL,
    recurring_value_monthly      numeric(12, 2) NULL,
    expected_close_on            date NULL,
    owner_user_id                uuid NULL,
    closed_at                    timestamptz NULL,
    lost_reason                  text NULL,
    created_at                   timestamptz NOT NULL,
    updated_at                   timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_deals_tenant_stage ON geek_crm.deals (tenant_id, stage_id);

CREATE TABLE IF NOT EXISTS geek_crm.deal_stage_history (
    id             uuid PRIMARY KEY,
    tenant_id      uuid NOT NULL REFERENCES geek_crm.tenants (id),
    deal_id        uuid NOT NULL REFERENCES geek_crm.deals (id) ON DELETE CASCADE,
    from_stage_id  uuid NULL REFERENCES geek_crm.stages (id),
    to_stage_id    uuid NOT NULL REFERENCES geek_crm.stages (id),
    changed_at     timestamptz NOT NULL,
    changed_by     text NOT NULL
);

CREATE TABLE IF NOT EXISTS geek_crm.deal_participants (
    id              uuid PRIMARY KEY,
    tenant_id       uuid NOT NULL REFERENCES geek_crm.tenants (id),
    deal_id         uuid NOT NULL REFERENCES geek_crm.deals (id) ON DELETE CASCADE,
    participant_id  uuid NOT NULL REFERENCES geek_crm.participants (id) ON DELETE CASCADE,
    role            text NOT NULL,
    CONSTRAINT ck_deal_participants_role CHECK (role IN
        ('primary_contact', 'decision_maker', 'influencer', 'billing', 'other')),
    CONSTRAINT ux_deal_participants UNIQUE (deal_id, participant_id, role)
);

-- Everything that happened: calls, texts, voicemails, emails, meetings, notes, tasks.
CREATE TABLE IF NOT EXISTS geek_crm.activities (
    id                uuid PRIMARY KEY,
    tenant_id         uuid NOT NULL REFERENCES geek_crm.tenants (id),
    participant_id    uuid NULL REFERENCES geek_crm.participants (id) ON DELETE SET NULL,
    deal_id           uuid NULL REFERENCES geek_crm.deals (id) ON DELETE SET NULL,
    channel           text NOT NULL,
    direction         text NULL,
    occurred_at       timestamptz NOT NULL,
    subject           text NULL,
    body              text NULL,
    summary           text NULL,
    transcript        text NULL,
    recording_url     text NULL,
    duration_seconds  integer NULL,
    outcome           text NULL,
    external_ref      text NULL,
    due_at            timestamptz NULL,
    completed_at      timestamptz NULL,
    created_by        text NOT NULL,
    created_at        timestamptz NOT NULL,
    CONSTRAINT ck_activities_channel CHECK (channel IN ('call', 'sms', 'voicemail', 'email', 'meeting', 'note', 'task')),
    CONSTRAINT ck_activities_direction CHECK (direction IS NULL OR direction IN ('inbound', 'outbound')),
    CONSTRAINT ck_activities_outcome CHECK (outcome IS NULL OR outcome IN
        ('answered', 'missed', 'texted_back', 'voicemail_left', 'booked', 'no_show', 'completed', 'failed'))
);
CREATE INDEX IF NOT EXISTS ix_activities_participant ON geek_crm.activities (participant_id, occurred_at DESC);
-- Twilio retries a webhook; the same CallSid/MessageSid + channel + outcome is one activity.
CREATE UNIQUE INDEX IF NOT EXISTS ux_activities_external
    ON geek_crm.activities (tenant_id, external_ref, channel, outcome) WHERE external_ref IS NOT NULL;

CREATE TABLE IF NOT EXISTS geek_crm.property_definitions (
    id            uuid PRIMARY KEY,
    tenant_id     uuid NOT NULL REFERENCES geek_crm.tenants (id),
    object_type   text NOT NULL,
    key           text NOT NULL,
    label         text NOT NULL,
    data_type     text NOT NULL,
    options       jsonb NULL,
    is_sensitive  boolean NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_property_definitions_object CHECK (object_type IN ('person', 'organization', 'deal')),
    CONSTRAINT ck_property_definitions_type CHECK (data_type IN ('text', 'number', 'date', 'bool', 'enum', 'multi_enum')),
    CONSTRAINT ux_property_definitions_key UNIQUE (tenant_id, object_type, key)
);

CREATE TABLE IF NOT EXISTS geek_crm.property_values (
    id             uuid PRIMARY KEY,
    tenant_id      uuid NOT NULL REFERENCES geek_crm.tenants (id),
    definition_id  uuid NOT NULL REFERENCES geek_crm.property_definitions (id) ON DELETE CASCADE,
    record_id      uuid NOT NULL,
    value          jsonb NOT NULL,
    updated_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_property_values UNIQUE (definition_id, record_id)
);

-- Industry lookups (U.S. government lists). Loaded separately; organizations do not require a match.
CREATE TABLE IF NOT EXISTS geek_crm.naics_codes (
    code   text PRIMARY KEY,
    title  text NOT NULL,
    level  integer NOT NULL
);
CREATE TABLE IF NOT EXISTS geek_crm.sic_codes (
    code   text PRIMARY KEY,
    title  text NOT NULL
);

-- ----- Seed: Geek @ Your Spot (tenant #1) and its default sales pipeline -----

INSERT INTO geek_crm.tenants (id, name, slug)
VALUES ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', 'Geek @ Your Spot', 'geek-at-your-spot')
ON CONFLICT (id) DO NOTHING;

INSERT INTO geek_crm.pipelines (id, tenant_id, name, is_default)
VALUES ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', 'Sales', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO geek_crm.stages (id, tenant_id, pipeline_id, name, position, outcome, probability_pct) VALUES
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'New',            1, 'open', 10),
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c02', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'Qualified',      2, 'open', 25),
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c03', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'Meeting booked', 3, 'open', 50),
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c04', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'Proposal sent',  4, 'open', 70),
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c05', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'Won',            5, 'won', 100),
    ('6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2c06', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01', '6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2b01', 'Lost',           6, 'lost', 0)
ON CONFLICT (id) DO NOTHING;
