-- Geek-Receptionist signup, decided by Jeff 2026-11-16 ("eliminate"): the tenant's county is no longer asked for. It was optional
-- and nothing read it: not the gate, billing, telephony or the receptionist's script. The column goes with the question.

ALTER TABLE geek_voice.tenants DROP COLUMN IF EXISTS county;
