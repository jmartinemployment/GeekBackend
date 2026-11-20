-- Geek-Receptionist, Zero Data Retention. geek_voice.appointments carried a caller's name, phone, email, service address and the
-- problem they described. That is call content, which this platform keeps none of, and docs/Geek-Receptionist-no-retention.md
-- already required these columns to be dropped and claimed enforcement by "the columns not existing" while they still existed and
-- GeekRepository still had a route that wrote them. Dropping them makes the claim true: a write of a caller's details now fails
-- loudly at the database instead of succeeding quietly.
--
-- What stays is metadata, which ZDR does keep: which tenant, which call, when the appointment is, which provider holds it, and the
-- provider's own event id. Who the appointment is with, and what it is about, live in the owner's own calendar, which is the
-- client's storage and never ours. Idempotent.

ALTER TABLE geek_voice.appointments
    DROP COLUMN IF EXISTS customer_name,
    DROP COLUMN IF EXISTS customer_phone,
    DROP COLUMN IF EXISTS customer_email,
    DROP COLUMN IF EXISTS service_address,
    DROP COLUMN IF EXISTS issue_description;
