-- Purpose-named presentation fields on post_translations (replaces type-named excerpt columns).

ALTER TABLE geek_blog.post_translations
    ADD COLUMN IF NOT EXISTS home_use_case_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS hero_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS newspaper_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS pillar_page_use_case_excerpt TEXT,
    ADD COLUMN IF NOT EXISTS advertisement TEXT;

-- Best-effort copy from legacy columns before drop (operators may wipe and republish).
UPDATE geek_blog.post_translations
SET home_use_case_excerpt = technical_article_excerpt
WHERE slug::text LIKE 'use-cases/%'
  AND home_use_case_excerpt IS NULL
  AND technical_article_excerpt IS NOT NULL;

UPDATE geek_blog.post_translations
SET hero_excerpt = blog_excerpt
WHERE slug::text LIKE 'blog/%'
  AND hero_excerpt IS NULL
  AND blog_excerpt IS NOT NULL;

UPDATE geek_blog.post_translations
SET hero_excerpt = tool_excerpt
WHERE slug::text LIKE 'tools/%'
  AND hero_excerpt IS NULL
  AND tool_excerpt IS NOT NULL;

UPDATE geek_blog.post_translations
SET advertisement = advertising_excerpt
WHERE advertisement IS NULL
  AND advertising_excerpt IS NOT NULL;

ALTER TABLE geek_blog.post_translations
    DROP COLUMN IF EXISTS blog_excerpt,
    DROP COLUMN IF EXISTS technical_article_excerpt,
    DROP COLUMN IF EXISTS tool_excerpt,
    DROP COLUMN IF EXISTS advertising_excerpt;
