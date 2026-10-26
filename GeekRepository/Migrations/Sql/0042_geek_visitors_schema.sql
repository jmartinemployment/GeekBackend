-- 0042: the website visitor store, geek_visitors, in GeekRepository's own database.
--
-- geekatyourspot.com sends each accepted beacon to GeekAPI, which writes it here through
-- repo/visitors/* and reads the day back for the 07:00 America/New_York report. The store was
-- first built (2026-10-25, never deployed) as a second Mongo database. That was wrong: Mongo is
-- the crawl store only, and every other product store is a schema in this database
-- (AGENTS.md, "Mongo is for GeekCrawler only; every other store is Supabase", Jeff 2026-10-26).
--
-- Raw SQL rather than an EF migration, matching geek_gtm and geek_blog: the schema is created and
-- maintained by Migrations/Sql/*.sql through SqlMigrationRunner, and VisitorsDbContext has no EF
-- migrations and no MigrationsHistoryTable. VisitorsDbContext maps every column named here, by
-- the snake_case of its property name, so the two are read against each other.
--
-- Concurrency: each beacon is one read-modify-write of a session under the "version" column,
-- which VisitorRepository uses as an optimistic concurrency token and bumps on every write. A
-- lost race is retried against the fresh row, so a score only ever rises, a scheduler step keeps
-- its first time, and a report claim or an intent mark lands once.
--
-- Retention: Postgres has no TTL index. GeekAPI's daily report job calls repo/visitors/retention/
-- purge once a day, which deletes sessions and visitors last seen more than 180 days ago; the
-- page and click rows go with their session through ON DELETE CASCADE. Drop counts and report
-- totals carry no visitor data and are kept.

CREATE SCHEMA IF NOT EXISTS geek_visitors;

-- One browser, by the persistent visitor UUID the site mints. The identity is the latest booking
-- or contact submission.
CREATE TABLE IF NOT EXISTS geek_visitors.visitors (
    id                  uuid PRIMARY KEY,
    first_seen_at       timestamptz NOT NULL,
    last_seen_at        timestamptz NOT NULL,
    session_count       integer NOT NULL DEFAULT 0,
    identity_first_name text NULL,
    identity_last_name  text NULL,
    identity_email      text NULL,
    identity_phone      text NULL,
    identity_source     text NULL,
    identified_at       timestamptz NULL,
    version             integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_visitors_last_seen
    ON geek_visitors.visitors (last_seen_at);

-- One visit, by the session UUID the browser minted. row_no is the store's insertion order and
-- the paging cursor of the session list.
CREATE TABLE IF NOT EXISTS geek_visitors.sessions (
    id                         uuid PRIMARY KEY,
    row_no                     bigint GENERATED ALWAYS AS IDENTITY,
    visitor_id                 uuid NOT NULL,
    visit_number               integer NOT NULL DEFAULT 0,
    started_at                 timestamptz NOT NULL,
    last_seen_at               timestamptz NOT NULL,
    ip                         text NOT NULL,
    additional_ips             text[] NOT NULL DEFAULT '{}',
    edge_region                text NULL,
    florida                    boolean NOT NULL DEFAULT false,

    geo_country                text NULL,
    geo_region                 text NULL,
    geo_city                   text NULL,
    geo_postal_code            text NULL,
    geo_latitude               double precision NULL,
    geo_longitude              double precision NULL,
    geo_timezone               text NULL,
    geo_continent              text NULL,

    browser_geo_latitude       double precision NULL,
    browser_geo_longitude      double precision NULL,
    browser_geo_accuracy_meters double precision NULL,
    browser_geo_at             timestamptz NULL,

    ua_raw                     text NOT NULL DEFAULT '',
    ua_browser                 text NULL,
    ua_browser_version         text NULL,
    ua_os                      text NULL,
    ua_os_version              text NULL,
    ua_device_type             text NULL,
    ua_brand                   text NULL,
    ua_model                   text NULL,

    ch_ua                      text NULL,
    ch_mobile                  text NULL,
    ch_platform                text NULL,
    accept_language            text NULL,
    dnt                        text NULL,

    screen_width               integer NULL,
    screen_height              integer NULL,
    screen_avail_width         integer NULL,
    screen_avail_height        integer NULL,
    viewport_width             integer NULL,
    viewport_height            integer NULL,
    device_pixel_ratio         double precision NULL,
    color_depth                integer NULL,
    orientation                text NULL,

    device_platform            text NULL,
    hardware_concurrency       integer NULL,
    device_memory              double precision NULL,
    max_touch_points           integer NULL,
    pointer                    text NULL,
    hover                      boolean NULL,

    languages                  text[] NULL,
    browser_timezone           text NULL,
    utc_offset_minutes         integer NULL,
    cookie_enabled             boolean NULL,
    pdf_viewer_enabled         boolean NULL,
    display_mode               text NULL,
    color_scheme               text NULL,
    reduced_motion             boolean NULL,

    platform_version           text NULL,
    hint_model                 text NULL,
    architecture               text NULL,
    bitness                    text NULL,
    full_version_list          text[] NULL,

    effective_type             text NULL,
    downlink                   double precision NULL,
    rtt                        integer NULL,
    save_data                  boolean NULL,

    landing_url                text NULL,
    referrer                   text NULL,
    utm_source                 text NULL,
    utm_medium                 text NULL,
    utm_campaign               text NULL,
    utm_term                   text NULL,
    utm_content                text NULL,
    gclid                      text NULL,
    gbraid                     text NULL,
    wbraid                     text NULL,
    fbclid                     text NULL,
    msclkid                    text NULL,
    li_fat_id                  text NULL,
    ttclid                     text NULL,

    scheduler_viewed_at        timestamptz NULL,
    scheduler_date_picked_at   timestamptz NULL,
    scheduler_slot_held_at     timestamptz NULL,
    scheduler_booked_at        timestamptz NULL,
    contact_submitted_at       timestamptz NULL,

    score                      double precision NOT NULL DEFAULT 0,
    intent_reached_at          timestamptz NULL,
    version                    integer NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_sessions_row_no
    ON geek_visitors.sessions (row_no);
CREATE INDEX IF NOT EXISTS ix_sessions_started
    ON geek_visitors.sessions (started_at, row_no);
CREATE INDEX IF NOT EXISTS ix_sessions_visitor_started
    ON geek_visitors.sessions (visitor_id, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_sessions_last_seen
    ON geek_visitors.sessions (last_seen_at);

-- One page view of a session, in arrival order (seq). At most 200 per session, held by the
-- repository, not by the schema.
CREATE TABLE IF NOT EXISTS geek_visitors.session_pages (
    session_id      uuid NOT NULL REFERENCES geek_visitors.sessions (id) ON DELETE CASCADE,
    page_view_id    text NOT NULL,
    seq             integer NOT NULL,
    path            text NOT NULL,
    query           text NULL,
    title           text NULL,
    entered_at      timestamptz NOT NULL,
    active_ms       bigint NOT NULL DEFAULT 0,
    max_scroll_pct  integer NOT NULL DEFAULT 0,
    lcp             double precision NULL,
    inp             double precision NULL,
    cls             double precision NULL,
    fcp             double precision NULL,
    ttfb            double precision NULL,
    PRIMARY KEY (session_id, page_view_id)
);

-- One click on a page view, in arrival order (seq). At most 50 per page, held by the repository.
CREATE TABLE IF NOT EXISTS geek_visitors.page_clicks (
    session_id      uuid NOT NULL,
    page_view_id    text NOT NULL,
    seq             integer NOT NULL,
    href            text NULL,
    link_id         text NULL,
    outbound        boolean NOT NULL DEFAULT false,
    clicked_at      timestamptz NOT NULL,
    PRIMARY KEY (session_id, page_view_id, seq),
    FOREIGN KEY (session_id, page_view_id)
        REFERENCES geek_visitors.session_pages (session_id, page_view_id) ON DELETE CASCADE
);

-- Requests the site's /api/pulse dropped, counted per New York day and reason.
CREATE TABLE IF NOT EXISTS geek_visitors.filter_day_drops (
    day         text NOT NULL,
    reason      text NOT NULL,
    drop_count  integer NOT NULL DEFAULT 0,
    version     integer NOT NULL DEFAULT 0,
    PRIMARY KEY (day, reason)
);

-- The claim and the outcome of one day's report build. The row is inserted to claim the day, so
-- two GeekAPI instances racing at 07:00 build it once; built_at and the totals land after.
CREATE TABLE IF NOT EXISTS geek_visitors.daily_reports (
    day                     text PRIMARY KEY,
    claimed_at              timestamptz NOT NULL,
    built_at                timestamptz NULL,
    visitors                integer NOT NULL DEFAULT 0,
    new_visitors            integer NOT NULL DEFAULT 0,
    returning_visitors      integer NOT NULL DEFAULT 0,
    sessions                integer NOT NULL DEFAULT 0,
    page_views              integer NOT NULL DEFAULT 0,
    median_active_seconds   integer NOT NULL DEFAULT 0,
    florida_sessions        integer NOT NULL DEFAULT 0,
    identified_visitors     integer NOT NULL DEFAULT 0,
    intent_sessions         integer NOT NULL DEFAULT 0,
    -- The retention purge the build ran: its counts, or the error it answered. A failed purge is
    -- read from here, not from a log.
    purged_at               timestamptz NULL,
    purged_sessions         integer NULL,
    purged_visitors         integer NULL,
    purge_error             text NULL
);
