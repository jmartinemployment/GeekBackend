-- Revert of 0030/0031: drops content_structure from post_translations.
-- geek_blog.post_translations/post_sections return to their original,
-- untouched state — content_structure work moves to a genuinely new,
-- isolated table instead, per the original instruction to leave existing
-- tables alone.

ALTER TABLE geek_blog.post_translations
    DROP COLUMN IF EXISTS content_structure;
