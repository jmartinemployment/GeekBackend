-- Isolated multi-language blog schema. Does not alter public or geek_seo tables.

CREATE SCHEMA IF NOT EXISTS geek_blog;

CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS ltree;

-- ── RBAC ─────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS geek_blog.users (
    id          SERIAL PRIMARY KEY,
    email       CITEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS geek_blog.roles (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS geek_blog.permissions (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS geek_blog.user_roles (
    user_id     INT NOT NULL REFERENCES geek_blog.users(id) ON DELETE CASCADE,
    role_id     INT NOT NULL REFERENCES geek_blog.roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS geek_blog.role_permissions (
    role_id       INT NOT NULL REFERENCES geek_blog.roles(id) ON DELETE CASCADE,
    permission_id INT NOT NULL REFERENCES geek_blog.permissions(id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

CREATE INDEX IF NOT EXISTS ix_geek_blog_user_roles_role_id
    ON geek_blog.user_roles (role_id);

CREATE INDEX IF NOT EXISTS ix_geek_blog_role_permissions_permission_id
    ON geek_blog.role_permissions (permission_id);

-- ── Posts (slug lives in translations, not here) ─────────────────────────────

CREATE TABLE IF NOT EXISTS geek_blog.posts (
    id           SERIAL PRIMARY KEY,
    post_type    VARCHAR(50) NOT NULL DEFAULT 'BlogPosting',
    author_id    INT REFERENCES geek_blog.users(id) ON DELETE SET NULL,
    status       VARCHAR(20) NOT NULL DEFAULT 'draft',
    published_at TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_post_type
    ON geek_blog.posts (post_type);

CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_status_published
    ON geek_blog.posts (status, published_at DESC)
    WHERE status = 'published';

-- ── Localized translations ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS geek_blog.post_translations (
    id               SERIAL PRIMARY KEY,
    post_id          INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    language_code    VARCHAR(10) NOT NULL,
    slug             CITEXT NOT NULL,
    title            TEXT NOT NULL,
    body             TEXT NOT NULL DEFAULT '',
    post_type        VARCHAR(50) NOT NULL,
    schema_metadata  JSONB NOT NULL DEFAULT '{}'::jsonb,
    search_vector    TSVECTOR,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_geek_blog_post_translations_lang_slug UNIQUE (language_code, slug),
    CONSTRAINT uq_geek_blog_post_translations_post_lang UNIQUE (post_id, language_code),
    CONSTRAINT chk_geek_blog_schema_metadata_type_matches
        CHECK (schema_metadata->>'@type' = post_type)
);

CREATE INDEX IF NOT EXISTS ix_geek_blog_post_translations_post_id
    ON geek_blog.post_translations (post_id);

CREATE INDEX IF NOT EXISTS ix_geek_blog_post_translations_schema_metadata
    ON geek_blog.post_translations USING GIN (schema_metadata jsonb_path_ops);

CREATE INDEX IF NOT EXISTS ix_geek_blog_post_translations_search_vector
    ON geek_blog.post_translations USING GIN (search_vector);

-- ── Tags ─────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS geek_blog.tags (
    id   SERIAL PRIMARY KEY,
    slug CITEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS geek_blog.tag_translations (
    tag_id        INT NOT NULL REFERENCES geek_blog.tags(id) ON DELETE CASCADE,
    language_code VARCHAR(10) NOT NULL,
    name          TEXT NOT NULL,
    PRIMARY KEY (tag_id, language_code)
);

CREATE TABLE IF NOT EXISTS geek_blog.post_tags (
    post_id INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    tag_id  INT NOT NULL REFERENCES geek_blog.tags(id) ON DELETE CASCADE,
    PRIMARY KEY (post_id, tag_id)
);

-- ── Hierarchical comments (ltree) ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS geek_blog.comments (
    id         SERIAL PRIMARY KEY,
    post_id    INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    user_id    INT REFERENCES geek_blog.users(id) ON DELETE SET NULL,
    content    TEXT NOT NULL,
    path       LTREE NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_geek_blog_comments_post_id
    ON geek_blog.comments (post_id);

CREATE INDEX IF NOT EXISTS ix_geek_blog_comments_path
    ON geek_blog.comments USING GIST (path);

-- ── Sync denormalized post_type from parent posts ────────────────────────────

CREATE OR REPLACE FUNCTION geek_blog.sync_translation_post_type()
RETURNS TRIGGER AS $$
BEGIN
    SELECT p.post_type INTO NEW.post_type
    FROM geek_blog.posts p
    WHERE p.id = NEW.post_id;

    IF NEW.post_type IS NULL THEN
        RAISE EXCEPTION 'post_id % does not exist in geek_blog.posts', NEW.post_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_geek_blog_sync_translation_post_type ON geek_blog.post_translations;
CREATE TRIGGER trg_geek_blog_sync_translation_post_type
    BEFORE INSERT OR UPDATE OF post_id ON geek_blog.post_translations
    FOR EACH ROW
    EXECUTE FUNCTION geek_blog.sync_translation_post_type();

-- ── Full-text search: title (A), body (B), schema_metadata strings (C) ───────

CREATE OR REPLACE FUNCTION geek_blog.resolve_ts_config(lang VARCHAR)
RETURNS regconfig AS $$
BEGIN
    RETURN CASE lang
        WHEN 'en' THEN 'english'::regconfig
        WHEN 'fr' THEN 'french'::regconfig
        WHEN 'de' THEN 'german'::regconfig
        WHEN 'es' THEN 'spanish'::regconfig
        WHEN 'it' THEN 'italian'::regconfig
        WHEN 'pt' THEN 'portuguese'::regconfig
        WHEN 'nl' THEN 'dutch'::regconfig
        WHEN 'ru' THEN 'russian'::regconfig
        WHEN 'sv' THEN 'swedish'::regconfig
        WHEN 'no' THEN 'norwegian'::regconfig
        WHEN 'da' THEN 'danish'::regconfig
        ELSE 'simple'::regconfig
    END;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION geek_blog.update_post_translation_search_vector()
RETURNS TRIGGER AS $$
DECLARE
    ts_config regconfig;
    schema_vec tsvector;
BEGIN
    ts_config := geek_blog.resolve_ts_config(NEW.language_code);

    IF NEW.schema_metadata IS NOT NULL AND NEW.schema_metadata <> '{}'::jsonb THEN
        schema_vec := jsonb_to_tsvector(
            ts_config,
            NEW.schema_metadata,
            'strict $.** ? (@.type() == "string")'::jsonpath
        );
    ELSE
        schema_vec := ''::tsvector;
    END IF;

    NEW.search_vector :=
        setweight(to_tsvector(ts_config, COALESCE(NEW.title, '')), 'A') ||
        setweight(to_tsvector(ts_config, COALESCE(NEW.body, '')), 'B') ||
        setweight(schema_vec, 'C');

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_geek_blog_post_translation_search_vector ON geek_blog.post_translations;
CREATE TRIGGER trg_geek_blog_post_translation_search_vector
    BEFORE INSERT OR UPDATE OF title, body, schema_metadata, language_code
    ON geek_blog.post_translations
    FOR EACH ROW
    EXECUTE FUNCTION geek_blog.update_post_translation_search_vector();

-- ── Seed baseline permissions ────────────────────────────────────────────────

INSERT INTO geek_blog.permissions (name) VALUES
    ('blog:read'),
    ('blog:write'),
    ('blog:comment'),
    ('blog:moderate')
ON CONFLICT (name) DO NOTHING;

INSERT INTO geek_blog.roles (name) VALUES
    ('reader'),
    ('author'),
    ('editor'),
    ('admin')
ON CONFLICT (name) DO NOTHING;

INSERT INTO geek_blog.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM geek_blog.roles r
CROSS JOIN geek_blog.permissions p
WHERE r.name = 'admin'
ON CONFLICT DO NOTHING;

INSERT INTO geek_blog.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM geek_blog.roles r
JOIN geek_blog.permissions p ON p.name IN ('blog:read', 'blog:comment')
WHERE r.name = 'reader'
ON CONFLICT DO NOTHING;

INSERT INTO geek_blog.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM geek_blog.roles r
JOIN geek_blog.permissions p ON p.name IN ('blog:read', 'blog:write', 'blog:comment')
WHERE r.name = 'author'
ON CONFLICT DO NOTHING;
