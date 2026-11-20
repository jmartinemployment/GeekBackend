-- Geek-Receptionist, 2026-11-20. Jeff: "The application is a SaaS application, the variables are client data, shouldn't they be
-- encrypted?" The four personal details this product stores become ciphertext GeekAPI produced, like every credential column
-- already is: a business's own contact address and number, the number its receptionist transfers a caller to, and the address its
-- end-of-call messages go to. The other fourteen receptionist settings stay in the clear - they are behavioural flags, hours, a
-- greeting and a prompt, read on every call, and encrypting a boolean buys nothing.
--
-- WHY THERE IS NO BACKFILL HERE. The key is VOICE_ENCRYPTION_KEY and it lives in GeekAPI; GeekRepository holds ciphertext it never
-- decrypts and this database has no key at all. So SQL cannot encrypt these values, and a migration that pretended to would either
-- store them bare under an "_encrypted" name - a name read as a claim, which is exactly what this codebase forbids - or quietly lose
-- them. It does neither: the new columns are added empty, and a row written before this migration therefore has no readable contact
-- details. GeekAPI fails loudly on such a row rather than substituting a blank address (VoicePersonalDetails.Open returns not_found,
-- and the call is answered with no summary arranged rather than a summary sent somewhere else).
--
-- WHAT AN OPERATOR MUST DO. The product is not launched and has no customers, so the only rows affected are test signups. Delete
-- them and sign up again; nothing here deletes a tenant on its own, because dropping a column is reversible attention and deleting
-- a business's row is not. Pre-existing storage_connections and tenant_mcp_connections rows are in the same position for a different
-- reason: their bucket keys were sealed under the MCP OAuth purposes and the sealed format has gained a version byte, so they must be
-- re-entered through the dashboard. Idempotent.

ALTER TABLE geek_voice.tenants
    ADD COLUMN IF NOT EXISTS contact_email_encrypted TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS contact_phone_encrypted TEXT NOT NULL DEFAULT '';

-- The default existed only so the column could be added to a table that already has rows. A new tenant always arrives with both,
-- and ValidateCreateTenant refuses one that does not, so nothing may fall back to an empty string from here on.
ALTER TABLE geek_voice.tenants
    ALTER COLUMN contact_email_encrypted DROP DEFAULT,
    ALTER COLUMN contact_phone_encrypted DROP DEFAULT;

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS transfer_target_number_encrypted    TEXT NULL,
    ADD COLUMN IF NOT EXISTS message_destination_email_encrypted TEXT NULL;

-- The plaintext columns go in the same migration as the ones that replace them. Leaving them for a later migration would leave a
-- readable copy of every number and address beside the sealed one, and leaving the names behind would leave something to be read as
-- evidence that the plaintext path is still live.
ALTER TABLE geek_voice.tenants
    DROP COLUMN IF EXISTS contact_email,
    DROP COLUMN IF EXISTS contact_phone;

ALTER TABLE geek_voice.receptionist_configs
    DROP COLUMN IF EXISTS transfer_target_number,
    DROP COLUMN IF EXISTS message_destination_email;
