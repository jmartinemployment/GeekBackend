using Dapper;
using GeekRepository.Infrastructure;
using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

/// <summary>
/// Raw SQL for niche analytics dashboards. Column names must match EF migrations (PascalCase quoted identifiers).
/// </summary>
public sealed class NicheAnalyticsDapperRepository(IDbConnectionFactory db) : INicheAnalyticsDapperRepository
{
    public async Task<Result<NicheProfileSummary?>> GetProfileSummaryAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<NicheProfileSummary>("""
                SELECT
                    "Id",
                    "Domain",
                    "PrimaryNiche",
                    "TopicalAuthorityScore",
                    "TotalPillarsIdentified" AS "TotalPillars",
                    "PillarsCovered",
                    "PillarsGap",
                    "CompetitionLevel",
                    "AnalyzedAt",
                    "Status"
                FROM geek_seo.niche_profiles
                WHERE "Id" = @ProfileId
                """, new { ProfileId = profileId });

            return Result<NicheProfileSummary?>.Success(row);
        }
        catch (Exception ex)
        {
            return Result<NicheProfileSummary?>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<PillarCoverageMatrix>>> GetCoverageMatrixAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();
            var rows = await conn.QueryAsync<PillarCoverageMatrix>("""
                SELECT
                    p."Id" AS "PillarId",
                    p."PillarTopic",
                    p."PrimaryKeyword",
                    p."SearchVolume",
                    p."KeywordDifficulty",
                    p."CoverageScore",
                    p."CoveredSubtopicCount" AS "CoveredSubtopics",
                    p."RequiredSubtopicCount" AS "TotalSubtopics",
                    (p."RequiredSubtopicCount" - p."CoveredSubtopicCount") AS "GapSubtopics",
                    p."CoverageStatus",
                    p."StrategicPriority",
                    EXISTS (
                        SELECT 1
                        FROM geek_seo.niche_subtopics s
                        WHERE s."PillarId" = p."Id" AND s."IsQuickWin" = TRUE
                    ) AS "HasQuickWins"
                FROM geek_seo.niche_pillars p
                WHERE p."NicheProfileId" = @ProfileId
                ORDER BY p."DisplayOrder" ASC
                """, new { ProfileId = profileId });

            return Result<IReadOnlyList<PillarCoverageMatrix>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<PillarCoverageMatrix>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<TopicalGapSummary>>> GetTopicalGapsAsync(
        Guid profileId,
        bool quickWinsOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();
            var rows = await conn.QueryAsync<TopicalGapSummary>("""
                SELECT
                    s."Id" AS "SubtopicId",
                    p."PillarTopic",
                    s."SubtopicTitle",
                    s."TargetKeyword",
                    s."SearchVolume",
                    s."KeywordDifficulty",
                    s."IsQuickWin",
                    s."RecommendedFormat",
                    s."FixEffort"
                FROM geek_seo.niche_subtopics s
                INNER JOIN geek_seo.niche_pillars p ON s."PillarId" = p."Id"
                WHERE p."NicheProfileId" = @ProfileId
                  AND s."CoverageStatus" = 'gap'
                  AND (@QuickWinsOnly = FALSE OR s."IsQuickWin" = TRUE)
                ORDER BY s."KeywordDifficulty" ASC, s."SearchVolume" DESC
                """, new { ProfileId = profileId, QuickWinsOnly = quickWinsOnly });

            return Result<IReadOnlyList<TopicalGapSummary>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<TopicalGapSummary>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<AuthorityProgressPoint>>> GetAuthorityProgressAsync(
        Guid projectId,
        int months = 12,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();
            var cutoff = DateTimeOffset.UtcNow.AddMonths(-months);

            var rows = await conn.QueryAsync<AuthorityProgressPoint>("""
                SELECT
                    np."AnalyzedAt" AS "SnapshotDate",
                    np."TopicalAuthorityScore",
                    np."PillarsCovered",
                    (
                        SELECT COUNT(*)::int
                        FROM geek_seo.niche_subtopics s
                        INNER JOIN geek_seo.niche_pillars pl ON s."PillarId" = pl."Id"
                        WHERE pl."NicheProfileId" = np."Id"
                          AND s."CoverageStatus" = 'covered'
                    ) AS "TotalSubtopicsCovered",
                    np."PillarsGap" AS "TotalGaps"
                FROM geek_seo.niche_profiles np
                WHERE np."ProjectId" = @ProjectId
                  AND np."Status" = 'complete'
                  AND np."AnalyzedAt" >= @Cutoff
                ORDER BY np."AnalyzedAt" ASC
                """, new { ProjectId = projectId, Cutoff = cutoff });

            return Result<IReadOnlyList<AuthorityProgressPoint>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<AuthorityProgressPoint>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<CompetitorNicheOverlap>>> GetCompetitorOverlapAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();

            var rows = await conn.QueryAsync<CompetitorNicheOverlap>("""
                SELECT
                    c."Domain" AS "CompetitorDomain",
                    c."PillarsRanking" AS "SharedPillarCount",
                    0 AS "CompetitorOnlyPillarCount",
                    GREATEST(p."TotalPillarsIdentified" - c."PillarsRanking", 0) AS "OurOnlyPillarCount",
                    c."EstimatedAuthorityScore"
                FROM geek_seo.niche_competitors c
                INNER JOIN geek_seo.niche_profiles p ON c."NicheProfileId" = p."Id"
                WHERE c."NicheProfileId" = @ProfileId
                ORDER BY c."SerpPresence" DESC
                """, new { ProfileId = profileId });

            return Result<IReadOnlyList<CompetitorNicheOverlap>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CompetitorNicheOverlap>>.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<EntityCoverageReport>>> GetEntityCoverageAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = db.CreateConnection();

            var rows = await conn.QueryAsync<EntityCoverageReport>("""
                SELECT
                    "EntityName",
                    "EntityType",
                    "MentionFrequency",
                    "PresentOnDomain",
                    COALESCE(array_length("AssociatedPillarIds", 1), 0) AS "AssociatedPillarCount"
                FROM geek_seo.niche_entities
                WHERE "NicheProfileId" = @ProfileId
                ORDER BY "MentionFrequency" DESC
                """, new { ProfileId = profileId });

            return Result<IReadOnlyList<EntityCoverageReport>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<EntityCoverageReport>>.Failure(ex.Message);
        }
    }
}
