-- Replace jsonb_to_tsvector(jsonpath) trigger logic (requires PG 17+) with portable
-- extraction of common schema.org string fields for full-text search.

CREATE OR REPLACE FUNCTION geek_blog.update_post_translation_search_vector()
RETURNS TRIGGER AS $$
DECLARE
    ts_config regconfig;
    schema_text text;
    schema_vec tsvector;
BEGIN
    ts_config := geek_blog.resolve_ts_config(NEW.language_code);

    IF NEW.schema_metadata IS NOT NULL AND NEW.schema_metadata <> '{}'::jsonb THEN
        schema_text := trim(both from concat_ws(
            ' ',
            NEW.schema_metadata->>'headline',
            NEW.schema_metadata->>'description',
            NEW.schema_metadata->>'articleSection',
            NEW.schema_metadata->>'name',
            NEW.schema_metadata->>'abstract'
        ));
        schema_vec := to_tsvector(ts_config, COALESCE(schema_text, ''));
    ELSE
        schema_vec := ''::tsvector;
    END IF;

    NEW.search_vector :=
        setweight(to_tsvector(ts_config, COALESCE(NEW.title, '')), 'A') ||
        setweight(to_tsvector(ts_config, COALESCE(NEW.body, '')), 'B') ||
        setweight(schema_vec, 'C');

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
