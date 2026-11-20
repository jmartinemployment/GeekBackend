-- Geek-Receptionist, bring-your-own-storage (plans/client-and-app-plan.md, "Call logs and transcripts (BYOS)"): Google Drive is a
-- connection a tenant makes like its calendar, so it joins the providers the connection table accepts. The appointments table keeps
-- its own list unchanged: a calendar provider is the only thing an appointment can come from, and Drive is not one. Idempotent.
--
-- Corrected 2026-11-20, having never run. It named geek_voice.mcp_connections, and the table is geek_voice.tenant_mcp_connections
-- (0060:84). A migration naming a table that does not exist raises 42P01, and SqlMigrationRunner stops the application and rethrows,
-- so this did not merely fail - it would have prevented GeekRepository from starting, and taken 0079, 0080 and 0081 with it.
--
-- The constraint name was wrong in the same way. 0060 declares the CHECK inline on the column, so Postgres names it
-- tenant_mcp_connections_provider_check; dropping "mcp_connections_provider_check" matched nothing. Had only the table name been
-- fixed, the original CHECK would have survived alongside the new one and google_drive would still have been refused by the database
-- while every layer above accepted it. Both names are dropped here so this is correct whether or not it has ever been applied.

ALTER TABLE geek_voice.tenant_mcp_connections
    DROP CONSTRAINT IF EXISTS tenant_mcp_connections_provider_check;

ALTER TABLE geek_voice.tenant_mcp_connections
    DROP CONSTRAINT IF EXISTS ck_tenant_mcp_connections_provider;

ALTER TABLE geek_voice.tenant_mcp_connections
    ADD CONSTRAINT ck_tenant_mcp_connections_provider
    CHECK (provider IN ('google_calendar', 'outlook', 'cal_com', 'hubspot', 'google_drive'));
