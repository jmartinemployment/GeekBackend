-- Geek-Receptionist, per-business end-of-call summary email. Each business chooses whether its receptionist sends one, where it
-- goes, whether the transcript is included, and whether a call with no real caller speech still sends. All four default off, so
-- an existing configuration sends nothing until its owner opts in. The destination is the business's own address; the email body
-- is built in memory at hang-up and is never stored here. Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS message_email_enabled     BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS message_destination_email TEXT    NULL,
    ADD COLUMN IF NOT EXISTS include_transcript        BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS notify_on_abandoned_calls BOOLEAN NOT NULL DEFAULT FALSE;
