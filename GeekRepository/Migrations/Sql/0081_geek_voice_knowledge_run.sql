-- Geek-Receptionist: the business's own indexed site, so the receptionist can answer a caller's question about the business
-- instead of taking a message for everything that is not a booking.
--
-- This is the crawl run of the business's website, already indexed into Qdrant on the Hostinger VPS by the Library
-- (EnqueueIndexAsync). Nothing is embedded here and no vector is stored here: the column is a scope, and the Library owns the
-- search. It is the same scope the content pipeline retrieves by, which is why there is no new concept in it.
--
-- NULL means the business has no indexed site. That is the ordinary case before the signup crawl has run, and it gives the
-- receptionist no lookup tool at all rather than one that always answers nothing.

ALTER TABLE geek_voice.receptionist_configs
    ADD COLUMN IF NOT EXISTS knowledge_run_id UUID NULL;
