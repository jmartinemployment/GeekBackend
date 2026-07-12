-- Allow @graph JSON-LD roots when the graph contains the expected post_type entity.
-- PostgreSQL CHECK constraints cannot contain subqueries; use an immutable helper.

CREATE OR REPLACE FUNCTION geek_blog.schema_metadata_matches_post_type(
    metadata jsonb,
    expected_post_type text)
RETURNS boolean
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT
        metadata->>'@type' = expected_post_type
        OR (
            metadata ? '@graph'
            AND EXISTS (
                SELECT 1
                FROM jsonb_array_elements(metadata->'@graph') AS graph_node
                WHERE graph_node->>'@type' = expected_post_type
            )
        );
$$;

ALTER TABLE geek_blog.post_translations
    DROP CONSTRAINT IF EXISTS chk_geek_blog_schema_metadata_type_matches;

ALTER TABLE geek_blog.post_translations
    ADD CONSTRAINT chk_geek_blog_schema_metadata_type_matches
        CHECK (geek_blog.schema_metadata_matches_post_type(schema_metadata, post_type));
