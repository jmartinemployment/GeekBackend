-- Geek-Receptionist: an appointment cannot come from HubSpot, because nothing books through HubSpot.
--
-- The baseline (0060) listed 'hubspot' among the providers an appointment may come from, and 0078 then widened the *connection*
-- table's list to admit Google Drive. The carve that did it was written as "everything that is not Drive is a calendar", which left a
-- CRM sitting in the appointments list under a comment saying a calendar is the only thing an appointment can come from. Jeff,
-- 2026-11-20: "Hubspot while it has a calendar, no it is a CRM, your filing."
--
-- HubSpot's meetings API is real, so this is not a statement about the vendor. It is a statement about us: no code books through it,
-- so an appointment row naming it could only have been written by a mistake, and the constraint should say so. The day booking through
-- HubSpot is built and tested end to end, this list grows again - deliberately, with the capability that earns it.
--
-- geek_voice.appointments has no rows naming 'hubspot' to migrate: nothing has ever written one, because only Google Calendar has an
-- OAuth path at all. Narrowing is therefore safe, and a row that somehow exists would fail the new constraint loudly rather than be
-- rewritten to something it is not.
--
-- Idempotent.

ALTER TABLE geek_voice.appointments
    DROP CONSTRAINT IF EXISTS appointments_mcp_provider_check;

ALTER TABLE geek_voice.appointments
    DROP CONSTRAINT IF EXISTS ck_voice_appointments_mcp_provider;

ALTER TABLE geek_voice.appointments
    ADD CONSTRAINT ck_voice_appointments_mcp_provider
    CHECK (mcp_provider IN ('google_calendar', 'outlook', 'cal_com'));
