-- Geek-Receptionist, decided by Jeff 2026-11-18: Retell AI is dropped from the product. A tenant's number is answered by GeekAPI's
-- own call route, and its calls are metered from Twilio's status callback, keyed by the Twilio call SID. Nothing reads or writes
-- the Retell agent, the Retell call id or the Retell voice any more. No business has signed up (the product is not launched), so
-- only test rows lose those values.

-- 1. The tenant's Retell agent and the starting settings' Retell voice.
ALTER TABLE geek_voice.tenant_telephony DROP COLUMN IF EXISTS retell_agent_id;
ALTER TABLE geek_voice.receptionist_configs DROP COLUMN IF EXISTS voice_id;

-- 2. Calls were keyed by the Retell call id (0062). Its check, its unique index and the column go.
ALTER TABLE geek_voice.call_logs DROP CONSTRAINT IF EXISTS ck_voice_calls_retell_call_id;
DROP INDEX IF EXISTS geek_voice.uq_voice_calls_retell_call;
ALTER TABLE geek_voice.call_logs DROP COLUMN IF EXISTS retell_call_id;

-- 3. The Twilio call SID is the key again: every call row carries one, and no two rows share one. The NOT VALID check holds every
--    row written from now on without scanning the rows already there, so it cannot stop GeekRepository from starting. The UNIQUE
--    constraint 0060 created on the column was never dropped; it is added here only on a database where it is missing, because
--    the upsert's ON CONFLICT (twilio_call_sid) needs it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_calls_twilio_call_sid') THEN
        ALTER TABLE geek_voice.call_logs ADD CONSTRAINT ck_voice_calls_twilio_call_sid CHECK (twilio_call_sid IS NOT NULL) NOT VALID;
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_index i
        JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
        WHERE i.indrelid = 'geek_voice.call_logs'::regclass
          AND i.indisunique
          AND i.indnatts = 1
          AND a.attname = 'twilio_call_sid') THEN
        ALTER TABLE geek_voice.call_logs ADD CONSTRAINT uq_voice_calls_twilio_call_sid UNIQUE (twilio_call_sid);
    END IF;
END
$$;
