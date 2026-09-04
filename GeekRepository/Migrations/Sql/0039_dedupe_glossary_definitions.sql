-- Dedupe glossary definitions created by re-running non-idempotent seed inserts.
-- Seed scripts used ON CONFLICT for terms but always INSERTed definitions; ~12
-- re-applies left identical rows. Newest add migrations (0036/0038) were clean.
--
-- After this: one row per (term_id, text), sort_order renumbered 0..n-1, and a
-- unique index so future duplicate inserts fail instead of stacking.
-- Future add-scripts should use WHERE NOT EXISTS / ON CONFLICT DO NOTHING on
-- definitions so re-runs stay idempotent.

BEGIN;

-- Keep the earliest row for each unique definition text per term.
DELETE FROM geek_glossary.term_definitions d
WHERE d.id NOT IN (
    SELECT MIN(id)
    FROM geek_glossary.term_definitions
    GROUP BY term_id, text
);

-- Renumber sort_order per term (stable: prior sort_order, then id).
WITH ranked AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY term_id
            ORDER BY sort_order, id
        ) - 1 AS new_order
    FROM geek_glossary.term_definitions
)
UPDATE geek_glossary.term_definitions d
SET sort_order = ranked.new_order
FROM ranked
WHERE d.id = ranked.id;

CREATE UNIQUE INDEX IF NOT EXISTS uq_geek_glossary_term_definitions_term_text
    ON geek_glossary.term_definitions (term_id, text);

COMMIT;
