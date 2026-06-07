-- Scalable niche persistence: topic candidate inventory + phase status columns
-- Mirrors GeekSeo.Persistence migration AddNicheScalablePersistence

ALTER TABLE geek_seo.niche_profiles
    ADD COLUMN IF NOT EXISTS "StructureStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "EnrichmentStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "ScanFingerprint" text,
    ADD COLUMN IF NOT EXISTS "ScanChangeScore" numeric(5,4),
    ADD COLUMN IF NOT EXISTS "PersistStage" text;

ALTER TABLE geek_seo.niche_pillars
    ADD COLUMN IF NOT EXISTS "CandidateId" uuid,
    ADD COLUMN IF NOT EXISTS "EnrichmentStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "EnrichedAt" timestamptz;

CREATE TABLE IF NOT EXISTS geek_seo.niche_topic_candidates (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "NicheProfileId" uuid NOT NULL,
    "Slug" text NOT NULL,
    "Name" text NOT NULL,
    "Confidence" numeric NOT NULL,
    "IsSelected" boolean NOT NULL,
    "ExclusionReason" text,
    "DedicatedPageUrl" text,
    "InternalLinkCount" integer NOT NULL,
    "ContentDepthScore" numeric NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "EvidenceJson" jsonb,
    "CreatedAt" timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_niche_topic_candidates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_niche_topic_candidates_niche_profiles_NicheProfileId"
        FOREIGN KEY ("NicheProfileId") REFERENCES geek_seo.niche_profiles ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_niche_topic_candidates_NicheProfileId"
    ON geek_seo.niche_topic_candidates ("NicheProfileId");

CREATE INDEX IF NOT EXISTS "IX_niche_topic_candidates_NicheProfileId_IsSelected"
    ON geek_seo.niche_topic_candidates ("NicheProfileId", "IsSelected");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_niche_topic_candidates_NicheProfileId_Slug"
    ON geek_seo.niche_topic_candidates ("NicheProfileId", "Slug");

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260607205248_AddNicheScalablePersistence', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;
