-- Listing excerpts and hero image URL (layout-owned; not derived from body at read time).

ALTER TABLE geek_blog.post_translations
    ADD COLUMN IF NOT EXISTS blog_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS technical_article_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS tool_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS advertising_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS hero_image_url TEXT;
