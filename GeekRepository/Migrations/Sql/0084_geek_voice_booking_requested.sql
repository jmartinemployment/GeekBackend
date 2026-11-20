-- Geek-Receptionist: the business answered, at signup, that it wants its receptionist to book appointments. Not the same column as
-- booking_enabled, and deliberately so.
--
-- booking_enabled is whether a call may write to the calendar, and turning it on is refused without a calendar that works
-- (BookingSettingController, ReceptionistConfigWriter): a receptionist shown as booking with nothing behind it offers every caller a
-- message instead, and the owner is never told. At signup no business has a calendar - connecting one is a Google sign-in whose
-- callback needs the owner's own token, and the owner's login is the last step of signup. So the appointments question could not be
-- asked in the form at all: the only answer storable there was no.
--
-- This column is the answer, kept separately from the capability. Jeff, 2026-11-20, choosing it over asking the question after
-- signup: every setting is asked in the onboarding form. The form stores the answer here; the first sign-in reads it, asks the owner
-- to connect their calendar, and switching booking on then is honouring an answer the business gave - not inferring consent from the
-- fact that a calendar exists, which is the thing booking_enabled exists to keep separate.
--
-- A business that answered no is left at FALSE and is never asked again by the dashboard. Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS booking_requested BOOLEAN NOT NULL DEFAULT FALSE;
