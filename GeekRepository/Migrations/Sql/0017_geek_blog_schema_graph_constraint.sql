-- Allow @graph JSON-LD roots when the graph contains the expected post_type entity.

ALTER TABLE geek_blog.post_translations
    DROP CONSTRAINT IF EXISTS chk_geek_blog_schema_metadata_type_matches;

ALTER TABLE geek_blog.post_translations
    ADD CONSTRAINT chk_geek_blog_schema_metadata_type_matches
        CHECK (
            schema_metadata->>'@type' = post_type
            OR (
                schema_metadata ? '@graph'
                AND EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements(schema_metadata->'@graph') AS graph_node
                    WHERE graph_node->>'@type' = post_type
                )
            )
        );
