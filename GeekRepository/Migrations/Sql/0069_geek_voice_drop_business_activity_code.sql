-- Geek-Receptionist signup, decided by Jeff 2026-11-18: the list of IRS business codes is gone from signup ("The business codes
-- are overwhelming and are vetoed"), and the field that held a tenant's code goes with it ("Drop/delete the old code column
-- field(s) from all occurences including the database and forms"). Nothing reads or writes it. No business has signed up (the
-- product is not launched), so only test signups lose a code; industry_vertical, the words the receptionist's script uses, stays.

ALTER TABLE geek_voice.tenants DROP CONSTRAINT IF EXISTS ck_voice_tenants_business_activity_code;
ALTER TABLE geek_voice.tenants DROP COLUMN IF EXISTS business_activity_code;
