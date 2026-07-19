-- Idempotent: backfills content_structure from existing post_sections rows.
-- Only touches rows where content_structure IS NULL, so re-running is safe and
-- doesn't clobber anything already populated. post_sections is left untouched —
-- it remains the source of truth until reads are cut over to content_structure.

UPDATE geek_blog.post_translations pt
SET content_structure = jsonb_build_object(
    'sections', COALESCE(
        (
            SELECT jsonb_agg(
                jsonb_build_object(
                    'sortOrder', ps.sort_order,
                    'headingTag', ps.heading_tag,
                    'headingText', ps.heading_text,
                    'bodyContent', ps.body_content,
                    'mediaUrl', ps.media_url,
                    'mediaAlt', ps.media_alt
                ) ORDER BY ps.sort_order
            )
            FROM geek_blog.post_sections ps
            WHERE ps.post_translation_id = pt.id
        ),
        '[]'::jsonb
    ),
    'mainBody', NULL
)
WHERE pt.content_structure IS NULL;
