-- Scalable site analysis persistence: topic candidate inventory + phase status columns
-- Mirrors GeekSeo.Persistence migration AddSiteAnalysisScalablePersistence

ALTER TABLE geek_seo.site_analysis_profiles
    ADD COLUMN IF NOT EXISTS "StructureStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "EnrichmentStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "ScanFingerprint" text,
    ADD COLUMN IF NOT EXISTS "ScanChangeScore" numeric(5,4),
    ADD COLUMN IF NOT EXISTS "PersistStage" text;

ALTER TABLE geek_seo.site_analysis_pillars
    ADD COLUMN IF NOT EXISTS "CandidateId" uuid,
    ADD COLUMN IF NOT EXISTS "EnrichmentStatus" text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "EnrichedAt" timestamptz;

CREATE TABLE IF NOT EXISTS geek_seo.site_analysis_topic_candidates (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "SiteAnalysisProfileId" uuid NOT NULL,
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
    CONSTRAINT "PK_site_analysis_topic_candidates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_site_analysis_topic_candidates_site_analysis_profiles_SiteAnalysisProfileId"
        FOREIGN KEY ("SiteAnalysisProfileId") REFERENCES geek_seo.site_analysis_profiles ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_site_analysis_topic_candidates_SiteAnalysisProfileId"
    ON geek_seo.site_analysis_topic_candidates ("SiteAnalysisProfileId");

CREATE INDEX IF NOT EXISTS "IX_site_analysis_topic_candidates_SiteAnalysisProfileId_IsSelected"
    ON geek_seo.site_analysis_topic_candidates ("SiteAnalysisProfileId", "IsSelected");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_site_analysis_topic_candidates_SiteAnalysisProfileId_Slug"
    ON geek_seo.site_analysis_topic_candidates ("SiteAnalysisProfileId", "Slug");

INSERT INTO geek_seo."__EFSeoMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260607205248_AddSiteAnalysisScalablePersistence', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;
