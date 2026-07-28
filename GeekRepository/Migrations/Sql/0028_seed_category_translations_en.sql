INSERT INTO geek_blog.category_translations (category_id, language_code, name)
SELECT c.id, 'en', c.slug
FROM geek_blog.categories c
ON CONFLICT (category_id, language_code) DO NOTHING;
