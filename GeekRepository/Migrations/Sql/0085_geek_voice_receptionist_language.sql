-- Geek-Receptionist: the language the receptionist speaks to a caller in.
--
-- en-US is ConversationRelay's own documented default for an unset `language` attribute, so this column makes explicit what every call
-- has been running on inherited rather than chosen. Nothing about an existing call changes.
--
-- The set of values this may hold is not a database constraint, deliberately. A language is only offerable once every sentence spoken
-- to a caller exists in it - two of those sentences are the consent disclosure, which is reviewed wording under Fla. Stat. 934.03 - and
-- that text lives in ReceptionistLanguages, which is the thing a reviewer reads and a test covers. A CHECK constraint here would be a
-- second list to keep in step with the first, and the drift between two lists is how the last format migration went wrong.
--
-- Idempotent.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS language TEXT NOT NULL DEFAULT 'en-US';
