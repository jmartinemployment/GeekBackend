-- Geek-Receptionist: whether a business lets its receptionist put an appointment on its calendar while the caller is on the line.
-- Off by default, and separate from having a calendar connected: a business may well want the receptionist to read its calendar to say
-- when it is free without letting it write to it. Reading needs only the connection; writing needs this as well. Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS booking_enabled BOOLEAN NOT NULL DEFAULT FALSE;
