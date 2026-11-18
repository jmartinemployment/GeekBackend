-- Geek-Receptionist, decided by Jeff 2026-11-18: the signup question "Is the business a legal, accounting, tax, payroll, finance,
-- insurance or education business?" is deleted, and with it the addendum a professional services business had to sign. Nothing
-- reads or writes the addendum any more. No business has signed up (the product is not launched), so only test rows lose those
-- values.

ALTER TABLE geek_voice.tenants DROP CONSTRAINT IF EXISTS ck_voice_tenants_addendum_pair;
ALTER TABLE geek_voice.tenants DROP COLUMN IF EXISTS addendum_ip_address;
ALTER TABLE geek_voice.tenants DROP COLUMN IF EXISTS addendum_signed_at;
ALTER TABLE geek_voice.tenants DROP COLUMN IF EXISTS requires_addendum;
