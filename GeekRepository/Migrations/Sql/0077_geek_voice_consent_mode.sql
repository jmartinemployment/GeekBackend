-- Geek-Receptionist, the plan's two-mode consent gate (plans/client-and-app-plan.md, "Upfront consent gate (Two-Mode Tenant
-- Setting)"). Which mode a business uses is the business's own choice, and the dashboard asks it; this is only what a row starts as
-- before anybody has been asked.
--
-- That starting value is strict keypress, changed by Jeff on 2026-11-20: "Change 0077_geek_voice_consent_mode.sql has DEFAULT
-- 'passive_disclosure' to the more explicited mode." It was passive, which meant a business that never opened the dashboard was
-- silently given the mode that asks nothing of a caller and deters nothing - and because this product bills by the minute, every
-- robocall the AI answered was a minute billed to that business. A key pressed is also consent somebody gave rather than consent
-- assumed from their not hanging up, which is the better evidence in an all-party state. Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS consent_mode TEXT NOT NULL DEFAULT 'strict_keypress';

-- Stated again, for a database where the column was already added with the old default. ADD COLUMN IF NOT EXISTS does nothing to a
-- column that is already there, including to its default, so without this the change would reach new databases only.
ALTER TABLE geek_voice.receptionist_configs
    ALTER COLUMN consent_mode SET DEFAULT 'strict_keypress';

ALTER TABLE geek_voice.receptionist_configs
    DROP CONSTRAINT IF EXISTS ck_receptionist_configs_consent_mode;

ALTER TABLE geek_voice.receptionist_configs
    ADD CONSTRAINT ck_receptionist_configs_consent_mode
    CHECK (consent_mode IN ('passive_disclosure', 'strict_keypress'));
