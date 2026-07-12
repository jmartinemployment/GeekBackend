-- CW project linkage on published posts (excerpt columns from 0018 unchanged).

ALTER TABLE geek_blog.post_translations
    ADD COLUMN IF NOT EXISTS source_project_id UUID,
    ADD COLUMN IF NOT EXISTS content_role VARCHAR(20),
    ADD COLUMN IF NOT EXISTS source_pillar_slug TEXT;

CREATE INDEX IF NOT EXISTS ix_post_translations_source_project_id
    ON geek_blog.post_translations (source_project_id)
    WHERE source_project_id IS NOT NULL;
