ALTER TABLE geek_blog.post_translations ADD COLUMN IF NOT EXISTS main_summary TEXT NOT NULL DEFAULT '';
ALTER TABLE geek_blog.post_translations ADD COLUMN IF NOT EXISTS blog_summary TEXT NOT NULL DEFAULT '';
ALTER TABLE geek_blog.post_translations ADD COLUMN IF NOT EXISTS advertising_summary TEXT NOT NULL DEFAULT '';
