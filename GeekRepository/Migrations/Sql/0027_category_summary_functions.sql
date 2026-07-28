CREATE INDEX IF NOT EXISTS ix_geek_blog_posts_type_published_category
    ON geek_blog.posts (post_type, is_published, category_id);

CREATE OR REPLACE FUNCTION geek_blog.get_home_page_pillars(p_lang text DEFAULT 'en')
RETURNS TABLE (category_slug text, category_name text, title text, summary text)
LANGUAGE sql STABLE AS $$
  SELECT c.slug, ct.name, pt.title, pt.home_summary
  FROM geek_blog.posts p
  JOIN geek_blog.post_translations pt ON pt.post_id = p.id AND pt.language_code = p_lang
  JOIN geek_blog.categories c ON c.id = p.category_id
  LEFT JOIN geek_blog.category_translations ct ON ct.category_id = c.id AND ct.language_code = p_lang
  WHERE p.post_type = 'Pillar' AND p.is_published = true
  ORDER BY c.slug, pt.title;
$$;

CREATE OR REPLACE FUNCTION geek_blog.get_pillar_summary_page(p_lang text DEFAULT 'en')
RETURNS TABLE (category_slug text, category_name text, title text, summary text)
LANGUAGE sql STABLE AS $$
  SELECT c.slug, ct.name, pt.title, pt.main_summary
  FROM geek_blog.posts p
  JOIN geek_blog.post_translations pt ON pt.post_id = p.id AND pt.language_code = p_lang
  JOIN geek_blog.categories c ON c.id = p.category_id
  LEFT JOIN geek_blog.category_translations ct ON ct.category_id = c.id AND ct.language_code = p_lang
  WHERE p.post_type = 'Pillar' AND p.is_published = true
  ORDER BY c.slug, pt.title;
$$;

-- DISTINCT ON (category, title) because the same tool name can be published as
-- separate rows across projects; picks the most recently updated one deterministically.
-- DISTINCT ON requires ORDER BY to start with the same expressions it distincts on
-- (c.slug, pt.title) before any tiebreaker (p.updated_at DESC) — preserve that order exactly.
CREATE OR REPLACE FUNCTION geek_blog.get_tools_summary_page(p_lang text DEFAULT 'en')
RETURNS TABLE (category_slug text, category_name text, title text, summary text)
LANGUAGE sql STABLE AS $$
  SELECT DISTINCT ON (c.slug, pt.title) c.slug, ct.name, pt.title, pt.main_summary
  FROM geek_blog.posts p
  JOIN geek_blog.post_translations pt ON pt.post_id = p.id AND pt.language_code = p_lang
  JOIN geek_blog.categories c ON c.id = p.category_id
  LEFT JOIN geek_blog.category_translations ct ON ct.category_id = c.id AND ct.language_code = p_lang
  WHERE p.post_type = 'Tool' AND p.is_published = true
  ORDER BY c.slug, pt.title, p.updated_at DESC;
$$;

CREATE OR REPLACE FUNCTION geek_blog.get_blog_summary_page(p_lang text DEFAULT 'en')
RETURNS TABLE (category_slug text, category_name text, title text, summary text)
LANGUAGE sql STABLE AS $$
  SELECT c.slug, ct.name, pt.title, pt.home_summary
  FROM geek_blog.posts p
  JOIN geek_blog.post_translations pt ON pt.post_id = p.id AND pt.language_code = p_lang
  JOIN geek_blog.categories c ON c.id = p.category_id
  LEFT JOIN geek_blog.category_translations ct ON ct.category_id = c.id AND ct.language_code = p_lang
  WHERE p.post_type = 'Blog' AND p.is_published = true
  ORDER BY c.slug, pt.title;
$$;
