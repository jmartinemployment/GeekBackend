-- Geek-Receptionist signup, decided by Jeff 2026-11-22: there is no declaration of authority at signup, and so no declarant title.
-- What a person agrees to is the AI disclosure and the Subscription Terms, and what is kept of them is the Terms statement they were shown.
--
-- Migrations 0087 and 0088 made declaration_statement and attestor_title_encrypted required. They are applied in production, and this
-- table is kept forever, so nothing is dropped: the two columns stay as they are for any row already written, and stop being required,
-- so a signup that no longer asks for them writes NULL rather than words nobody was shown. The checks on them (0087) pass on NULL.

ALTER TABLE geek_voice.signup_attestations ALTER COLUMN declaration_statement DROP NOT NULL;
ALTER TABLE geek_voice.signup_attestations ALTER COLUMN attestor_title_encrypted DROP NOT NULL;
