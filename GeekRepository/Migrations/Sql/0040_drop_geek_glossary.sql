-- 0040: drop the Postgres glossary.
--
-- Jeff, 2026-09-29: "NO GLOSSARY DATA IS TO BE QUERIES, PUT, POSTED OR GET FROM POSTGRES",
-- then "DROP AND DELETE POSTGRES GLOSSARY".
--
-- 48c9f0d removed every code path that could reach this schema -- both controllers,
-- HttpGlossaryRepository, IGlossaryRepository, the DTOs, GlossaryRepository's Dapper SQL, the
-- public-read middleware exemption, both DI registrations, and the 0034-0039 scripts that
-- created and seeded it. That left the tables in place with nothing able to read them. This
-- removes the tables and the rows.
--
-- The glossary's source of record is geekatyourspot/src/data/glossary/terms.ts, a file in the
-- site repo. Its own src/lib/glossary.ts states why: "Editing a term is a code change and a
-- deploy, not a database update." Nothing is lost here that is not held there.
--
-- CASCADE: term_definitions carries a foreign key to terms, and both carry indexes, so the
-- schema cannot be dropped a table at a time in one statement. CASCADE drops what the schema
-- contains and nothing outside it -- no other schema references geek_glossary.
--
-- IF EXISTS: 0034 created this schema and 0034 is deleted, so a database built from the current
-- scripts never has it. This must be a no-op there rather than an error that halts startup --
-- SqlMigrationRunner calls StopApplication() on a failed script.
--
-- citext stays. 0034 created the extension, but so do 0015 and 0022, and blog tables use it.

DROP SCHEMA IF EXISTS geek_glossary CASCADE;

-- And the bookkeeping for the six deleted scripts. Jeff, 2026-09-29: "I SEE NO REASON TO RETAIN
-- MIGRATIONS, FURTHER REMOVE ALL TRACES." I had kept these rows as a guard -- the runner skips a
-- name it finds recorded, so a restored file would not re-run -- but that is protection against
-- someone deliberately restoring a deleted file to rebuild a schema this script drops, which is
-- not a scenario worth leaving a trace for. The rows go.

DELETE FROM schema_migrations
WHERE script_name IN (
    '0034_geek_glossary_schema.sql',
    '0035_seed_geek_glossary.sql',
    '0036_add_glossary_speed_to_lead.sql',
    '0037_glossary_definition_quality.sql',
    '0038_add_glossary_lead_scoring.sql',
    '0039_dedupe_glossary_definitions.sql'
);
