-- Deletes the tenants whose sealed contact details are empty, which is exactly the rows written before 0082 added those columns.
--
-- 0082 added contact_email_encrypted and contact_phone_encrypted with DEFAULT '' so they could be added to a table that already
-- had rows, then dropped the plaintext columns. It did not back-fill, and could not: GeekRepository holds no encryption key, so
-- SQL cannot produce a sealed value. Every row older than 0082 therefore reads back blank, and GeekAPI refuses it rather than
-- substituting an address - correct, and permanently useless. The row cannot be repaired, only removed.
--
-- Approved by Jeff, 2026-11-20, asked as "delete one junk test row" and answered "Yes". It is one row: the single
-- validate-eligibility call that ever returned 200 (by curl, 2026-11-16). Its activation returned 400 and its three owner-login
-- attempts 404, so it has no subscription, no phone number and no owner login. This product is pre-launch and has no customers.
--
-- Written as a migration rather than run by hand so the state is enforced instead of noted: the previous version of this left an
-- instruction in a plan file, which is an assertion that no code backs.
--
-- Idempotent: after this runs, no row matches, and a tenant created from here on always arrives with both details sealed
-- (ValidateCreateTenant refuses one that does not), so this can never match a live tenant.

DELETE FROM geek_voice.tenants
WHERE contact_email_encrypted = ''
   OR contact_phone_encrypted = '';
