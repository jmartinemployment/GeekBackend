-- Isolated glossary schema for Geek at Your Spot term definitions.

CREATE SCHEMA IF NOT EXISTS geek_glossary;

CREATE EXTENSION IF NOT EXISTS citext;

CREATE TABLE IF NOT EXISTS geek_glossary.terms (
    id              SERIAL PRIMARY KEY,
    slug            CITEXT NOT NULL UNIQUE,
    title           TEXT NOT NULL,
    category        TEXT,
    short_summary   TEXT,
    status          VARCHAR(20) NOT NULL DEFAULT 'published'
                    CHECK (status IN ('draft', 'published')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_geek_glossary_terms_status
    ON geek_glossary.terms (status);

CREATE INDEX IF NOT EXISTS idx_geek_glossary_terms_slug
    ON geek_glossary.terms (slug);

CREATE TABLE IF NOT EXISTS geek_glossary.term_definitions (
    id              SERIAL PRIMARY KEY,
    term_id         INT NOT NULL REFERENCES geek_glossary.terms(id) ON DELETE CASCADE,
    sort_order      INT NOT NULL DEFAULT 0,
    part_of_speech  TEXT NOT NULL DEFAULT 'noun',
    text            TEXT NOT NULL,
    example         TEXT
);

CREATE INDEX IF NOT EXISTS idx_geek_glossary_term_definitions_term_id
    ON geek_glossary.term_definitions (term_id, sort_order);
