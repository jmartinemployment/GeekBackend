-- Geek-Receptionist, the plan's two-mode consent gate (plans/client-and-app-plan.md, "Upfront consent gate (Two-Mode Tenant
-- Setting)"). Passive disclosure is the plan's default: a brief spoken line before the AI engages. Strict keypress is the option for
-- an owner who prefers active opt-in, and is what every call did before this. Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS consent_mode TEXT NOT NULL DEFAULT 'passive_disclosure';

ALTER TABLE geek_voice.receptionist_configs
    DROP CONSTRAINT IF EXISTS ck_receptionist_configs_consent_mode;

ALTER TABLE geek_voice.receptionist_configs
    ADD CONSTRAINT ck_receptionist_configs_consent_mode
    CHECK (consent_mode IN ('passive_disclosure', 'strict_keypress'));
