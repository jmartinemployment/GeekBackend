CREATE SCHEMA IF NOT EXISTS geek_blog;
CREATE EXTENSION IF NOT EXISTS ltree;

DO $$ BEGIN
    CREATE TYPE geek_blog.post_type_enum AS ENUM ('Blog', 'Pillar', 'Tool');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE geek_blog.schema_type_enum AS ENUM ('BlogPosting', 'TechnicalArticle');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- RBAC (roles only)
CREATE TABLE IF NOT EXISTS geek_blog.roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE,
    normalized_name VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS geek_blog.users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(256) NOT NULL UNIQUE,
    password_hash VARCHAR(512) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS geek_blog.user_roles (
    user_id INT NOT NULL REFERENCES geek_blog.users(id) ON DELETE CASCADE,
    role_id INT NOT NULL REFERENCES geek_blog.roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);
CREATE INDEX IF NOT EXISTS ix_geek_blog_user_roles_role_id ON geek_blog.user_roles (role_id);

-- Taxonomy (i18n) — categories replaces departments
CREATE TABLE IF NOT EXISTS geek_blog.categories (
    id SERIAL PRIMARY KEY,
    slug VARCHAR(100) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS geek_blog.category_translations (
    id SERIAL PRIMARY KEY,
    category_id INT NOT NULL REFERENCES geek_blog.categories(id) ON DELETE CASCADE,
    language_code VARCHAR(10) NOT NULL,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    UNIQUE (category_id, language_code)
);

CREATE TABLE IF NOT EXISTS geek_blog.tags (
    id SERIAL PRIMARY KEY,
    slug VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS geek_blog.tag_translations (
    id SERIAL PRIMARY KEY,
    tag_id INT NOT NULL REFERENCES geek_blog.tags(id) ON DELETE CASCADE,
    language_code VARCHAR(10) NOT NULL,
    name VARCHAR(100) NOT NULL,
    UNIQUE (tag_id, language_code)
);

-- Core post (language-agnostic)
CREATE TABLE IF NOT EXISTS geek_blog.posts (
    id SERIAL PRIMARY KEY,
    slug VARCHAR(255) NOT NULL UNIQUE,
    post_type geek_blog.post_type_enum NOT NULL DEFAULT 'Blog',
    schema_type geek_blog.schema_type_enum NOT NULL DEFAULT 'BlogPosting',
    category_id INT NOT NULL REFERENCES geek_blog.categories(id) ON DELETE RESTRICT,
    author_id INT NOT NULL REFERENCES geek_blog.users(id) ON DELETE RESTRICT,
    cw_job_id VARCHAR(100),
    is_published BOOLEAN NOT NULL DEFAULT FALSE,
    published_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_category_id ON geek_blog.posts (category_id);
CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_post_type ON geek_blog.posts (post_type);
CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_published
    ON geek_blog.posts (is_published, published_at DESC) WHERE is_published = TRUE;
CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_cw_job_id
    ON geek_blog.posts (cw_job_id) WHERE cw_job_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS geek_blog.post_tags (
    post_id INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    tag_id INT NOT NULL REFERENCES geek_blog.tags(id) ON DELETE CASCADE,
    PRIMARY KEY (post_id, tag_id)
);

-- i18n translation + JSON-LD override + search_vector
CREATE TABLE IF NOT EXISTS geek_blog.post_translations (
    id SERIAL PRIMARY KEY,
    post_id INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    language_code VARCHAR(10) NOT NULL,
    title VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    meta_description TEXT,
    json_ld_override TEXT,
    search_vector TSVECTOR,
    UNIQUE (post_id, language_code)
);
CREATE INDEX IF NOT EXISTS ix_geek_blog_post_translations_post_id ON geek_blog.post_translations (post_id);
CREATE INDEX IF NOT EXISTS idx_post_translations_search ON geek_blog.post_translations USING gin(search_vector);

-- Presentation EAV, per-language
CREATE TABLE IF NOT EXISTS geek_blog.post_presentation_attributes (
    id SERIAL PRIMARY KEY,
    post_translation_id INT NOT NULL REFERENCES geek_blog.post_translations(id) ON DELETE CASCADE,
    attribute_key VARCHAR(100) NOT NULL,
    attribute_value TEXT NOT NULL,
    UNIQUE (post_translation_id, attribute_key)
);
CREATE INDEX IF NOT EXISTS idx_post_presentation_attributes_translation
    ON geek_blog.post_presentation_attributes (post_translation_id);

-- Flat HTML sections
CREATE TABLE IF NOT EXISTS geek_blog.post_sections (
    id SERIAL PRIMARY KEY,
    post_translation_id INT NOT NULL REFERENCES geek_blog.post_translations(id) ON DELETE CASCADE,
    sort_order INT NOT NULL,
    heading_tag VARCHAR(10),
    heading_text TEXT,
    body_content TEXT NOT NULL,
    media_url VARCHAR(512),
    media_alt VARCHAR(255),
    UNIQUE (post_translation_id, sort_order)
);
CREATE INDEX IF NOT EXISTS ix_geek_blog_post_sections_translation_id ON geek_blog.post_sections (post_translation_id);

-- Nested comments (ltree) with structured attachment
CREATE TABLE IF NOT EXISTS geek_blog.post_comments (
    id SERIAL PRIMARY KEY,
    post_id INT NOT NULL REFERENCES geek_blog.posts(id) ON DELETE CASCADE,
    user_id INT REFERENCES geek_blog.users(id) ON DELETE SET NULL,
    guest_name VARCHAR(100),
    content TEXT NOT NULL,
    attachment_url VARCHAR(512),
    path LTREE NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_comments_path ON geek_blog.post_comments USING gist(path);
CREATE INDEX IF NOT EXISTS ix_geek_blog_post_comments_post_id ON geek_blog.post_comments (post_id);

-- updated_at maintenance
CREATE OR REPLACE FUNCTION geek_blog.set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_geek_blog_posts_updated_at ON geek_blog.posts;
CREATE TRIGGER trg_geek_blog_posts_updated_at
    BEFORE UPDATE ON geek_blog.posts
    FOR EACH ROW EXECUTE FUNCTION geek_blog.set_updated_at();

-- Full-text search (title A, summary B, sections C)
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

CREATE OR REPLACE FUNCTION geek_blog.compute_post_translation_search_vector(p_translation_id INT)
RETURNS TSVECTOR AS $$
DECLARE
    ts_config regconfig;
    v_title TEXT;
    v_summary TEXT;
    sections_text TEXT;
BEGIN
    SELECT geek_blog.resolve_ts_config(pt.language_code), pt.title, pt.summary
    INTO ts_config, v_title, v_summary
    FROM geek_blog.post_translations pt
    WHERE pt.id = p_translation_id;

    SELECT string_agg(
        regexp_replace(COALESCE(ps.heading_text, ''), '<[^>]+>', ' ', 'g') || ' ' ||
        regexp_replace(ps.body_content, '<[^>]+>', ' ', 'g'),
        ' ' ORDER BY ps.sort_order
    )
    INTO sections_text
    FROM geek_blog.post_sections ps
    WHERE ps.post_translation_id = p_translation_id;

    RETURN
        setweight(to_tsvector(ts_config, COALESCE(v_title, '')), 'A') ||
        setweight(to_tsvector(ts_config, COALESCE(v_summary, '')), 'B') ||
        setweight(to_tsvector(ts_config, COALESCE(sections_text, '')), 'C');
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION geek_blog.sync_post_translation_search_vector()
RETURNS TRIGGER AS $$
DECLARE
    ts_config regconfig := geek_blog.resolve_ts_config(NEW.language_code);
    sections_text TEXT;
BEGIN
    -- BEFORE trigger: NEW row isn't committed yet, so compute from NEW's own
    -- fields directly instead of re-querying post_translations by NEW.id.
    -- Sections lookup is safe (FK requires the translation row to pre-exist,
    -- so on INSERT this is always empty; on UPDATE it reflects current sections).
    SELECT string_agg(
        regexp_replace(COALESCE(ps.heading_text, ''), '<[^>]+>', ' ', 'g') || ' ' ||
        regexp_replace(ps.body_content, '<[^>]+>', ' ', 'g'),
        ' ' ORDER BY ps.sort_order
    )
    INTO sections_text
    FROM geek_blog.post_sections ps
    WHERE ps.post_translation_id = NEW.id;

    NEW.search_vector :=
        setweight(to_tsvector(ts_config, COALESCE(NEW.title, '')), 'A') ||
        setweight(to_tsvector(ts_config, COALESCE(NEW.summary, '')), 'B') ||
        setweight(to_tsvector(ts_config, COALESCE(sections_text, '')), 'C');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_geek_blog_post_translation_search_vector ON geek_blog.post_translations;
CREATE TRIGGER trg_geek_blog_post_translation_search_vector
    BEFORE INSERT OR UPDATE OF title, summary, language_code ON geek_blog.post_translations
    FOR EACH ROW EXECUTE FUNCTION geek_blog.sync_post_translation_search_vector();

CREATE OR REPLACE FUNCTION geek_blog.sync_post_sections_search_vector()
RETURNS TRIGGER AS $$
DECLARE
    v_translation_id INT := COALESCE(NEW.post_translation_id, OLD.post_translation_id);
BEGIN
    UPDATE geek_blog.post_translations
    SET search_vector = geek_blog.compute_post_translation_search_vector(v_translation_id)
    WHERE id = v_translation_id;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_geek_blog_post_sections_search_vector ON geek_blog.post_sections;
CREATE TRIGGER trg_geek_blog_post_sections_search_vector
    AFTER INSERT OR DELETE OR UPDATE OF heading_text, body_content ON geek_blog.post_sections
    FOR EACH ROW EXECUTE FUNCTION geek_blog.sync_post_sections_search_vector();

-- Seed roles
INSERT INTO geek_blog.roles (name, normalized_name) VALUES
    ('reader', 'READER'), ('author', 'AUTHOR'), ('editor', 'EDITOR'), ('admin', 'ADMIN')
ON CONFLICT (name) DO NOTHING;
