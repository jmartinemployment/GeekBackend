-- Geek-Receptionist: drop the pgvector knowledge tables. They were a second vector store beside a
-- working one, and the second never ran.
--
-- geek_voice.knowledge_chunks held vector(384) columns and a cosine search, and
-- SearchVoiceKnowledgeCommand took a QueryEmbedding that nothing in GeekBackend ever produced: a grep
-- for an embedder across every .cs file returned one comment naming BAAI/bge-small-en-v1.5 and no code.
-- So no caller's question was ever turned into a vector and that search could not have served a single
-- call. Meanwhile Qdrant v1.13.4 and that same model have been running on the Hostinger VPS the whole
-- time (Geek-Crawler-Rag/docker-compose.yml).
--
-- Decided by Jeff, 2026-11-19. The tables go rather than sit dormant, because a surviving name is read
-- as a live claim: the next person to grep for "knowledge" would find a vector store and infer a
-- retrieval path that has never existed. Pre-launch, so no row is lost.
--
-- Where the receptionist's knowledge goes instead: Geek-Receptionist/plans/receptionist-owns-its-stack.md.
-- Idempotent; chunks first for the foreign key.

DROP TABLE IF EXISTS geek_voice.knowledge_chunks;
DROP TABLE IF EXISTS geek_voice.knowledge_documents;
