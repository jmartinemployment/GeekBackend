-- department_id on post_translations + normalized post_presentation surfaces.

ALTER TABLE geek_blog.post_translations
    ADD COLUMN IF NOT EXISTS department_id INTEGER REFERENCES public.departments(id);

-- Backfill department from slug segment 2 (use-cases/accounting/foo → accounting).
UPDATE geek_blog.post_translations pt
SET department_id = d.id
FROM public.departments d
WHERE pt.department_id IS NULL
  AND d.slug = split_part(pt.slug::text, '/', 2);

CREATE TABLE IF NOT EXISTS geek_blog.post_presentation (
    id SERIAL PRIMARY KEY,
    post_translation_id INTEGER NOT NULL REFERENCES geek_blog.post_translations(id) ON DELETE CASCADE,
    surface TEXT NOT NULL,
    copy TEXT NOT NULL,
    CONSTRAINT post_presentation_surface_unique UNIQUE (post_translation_id, surface)
);

CREATE INDEX IF NOT EXISTS idx_post_presentation_translation
    ON geek_blog.post_presentation (post_translation_id);

-- Migrate legacy wide columns from 0020 into presentation rows.
INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
SELECT pt.id, 'home_list', trim(pt.home_use_case_excerpt)
FROM geek_blog.post_translations pt
WHERE pt.home_use_case_excerpt IS NOT NULL AND trim(pt.home_use_case_excerpt) <> ''
ON CONFLICT (post_translation_id, surface) DO UPDATE SET copy = EXCLUDED.copy;

INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
SELECT pt.id, 'hero', trim(pt.hero_excerpt)
FROM geek_blog.post_translations pt
WHERE pt.hero_excerpt IS NOT NULL AND trim(pt.hero_excerpt) <> ''
ON CONFLICT (post_translation_id, surface) DO UPDATE SET copy = EXCLUDED.copy;

INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
SELECT pt.id, 'newspaper_wire', trim(pt.newspaper_excerpt)
FROM geek_blog.post_translations pt
WHERE pt.newspaper_excerpt IS NOT NULL AND trim(pt.newspaper_excerpt) <> ''
ON CONFLICT (post_translation_id, surface) DO UPDATE SET copy = EXCLUDED.copy;

INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
SELECT pt.id, 'pillar_page_content', trim(pt.pillar_page_use_case_excerpt)
FROM geek_blog.post_translations pt
WHERE pt.pillar_page_use_case_excerpt IS NOT NULL AND trim(pt.pillar_page_use_case_excerpt) <> ''
ON CONFLICT (post_translation_id, surface) DO UPDATE SET copy = EXCLUDED.copy;

INSERT INTO geek_blog.post_presentation (post_translation_id, surface, copy)
SELECT pt.id, 'advertisement', trim(pt.advertisement)
FROM geek_blog.post_translations pt
WHERE pt.advertisement IS NOT NULL AND trim(pt.advertisement) <> ''
ON CONFLICT (post_translation_id, surface) DO UPDATE SET copy = EXCLUDED.copy;

ALTER TABLE geek_blog.post_translations
    DROP COLUMN IF EXISTS home_use_case_excerpt,
    DROP COLUMN IF EXISTS hero_excerpt,
    DROP COLUMN IF EXISTS newspaper_excerpt,
    DROP COLUMN IF EXISTS pillar_page_use_case_excerpt,
    DROP COLUMN IF EXISTS advertisement;
