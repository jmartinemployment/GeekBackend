-- Idempotent: adds nullable content_structure jsonb to post_translations.
-- Additive only — post_sections stays untouched and remains the source of truth
-- until a backfill (0031) populates this column and reads are cut over.

ALTER TABLE geek_blog.post_translations
    ADD COLUMN IF NOT EXISTS content_structure jsonb;
