-- Geek-Receptionist, bring-your-own-storage (plans/client-and-app-plan.md, "Call logs and transcripts (BYOS)"): Google Drive is a
-- connection a tenant makes like its calendar, so it joins the providers the connection table accepts. The appointments table keeps
-- its own list unchanged: a calendar provider is the only thing an appointment can come from, and Drive is not one. Idempotent.

ALTER TABLE geek_voice.mcp_connections
    DROP CONSTRAINT IF EXISTS mcp_connections_provider_check;

ALTER TABLE geek_voice.mcp_connections
    ADD CONSTRAINT mcp_connections_provider_check
    CHECK (provider IN ('google_calendar', 'outlook', 'cal_com', 'hubspot', 'google_drive'));
