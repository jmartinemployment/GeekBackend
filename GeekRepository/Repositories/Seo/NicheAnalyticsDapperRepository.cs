using Dapper;
using GeekRepository.Infrastructure;
using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

public sealed class NicheAnalyticsDapperRepository(IDbConnectionFactory db) : INicheAnalyticsDapperRepository
{
    public async Task<Result<NicheProfileSummary?>> GetProfileSummaryAsync(
        Guid profileId, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<NicheProfileSummary>("""
            SELECT id,
                   domain,
                   primary_niche,
                   topical_authority_score,
                   total_pillars_identified AS total_pillars,
                   pillars_covered,
                   pillars_gap,
                   competition_level,
                   analyzed_at,
                   status
            FROM geek_seo.niche_profiles
            WHERE id = @ProfileId
            """, new { ProfileId = profileId });

        return Result<NicheProfileSummary?>.Success(row);
    }

    public async Task<Result<IReadOnlyList<PillarCoverageMatrix>>> GetCoverageMatrixAsync(
        Guid profileId, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<PillarCoverageMatrix>("""
            SELECT
                p.id                                                         AS pillar_id,
                p.pillar_topic,
                p.primary_keyword,
                p.search_volume,
                p.keyword_difficulty,
                p.coverage_score,
                p.covered_subtopic_count                                     AS covered_subtopics,
                p.required_subtopic_count                                    AS total_subtopics,
                (p.required_subtopic_count - p.covered_subtopic_count)       AS gap_subtopics,
                p.coverage_status,
                p.strategic_priority,
                EXISTS (
                    SELECT 1 FROM geek_seo.niche_subtopics s
                    WHERE s.pillar_id = p.id AND s.is_quick_win = TRUE
                )                                                            AS has_quick_wins
            FROM geek_seo.niche_pillars p
            WHERE p.niche_profile_id = @ProfileId
            ORDER BY p.display_order ASC
            """, new { ProfileId = profileId });

        return Result<IReadOnlyList<PillarCoverageMatrix>>.Success(rows.ToList());
    }

    public async Task<Result<IReadOnlyList<TopicalGapSummary>>> GetTopicalGapsAsync(
        Guid profileId, bool quickWinsOnly = false, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();
        var sql = """
            SELECT
                s.id            AS subtopic_id,
                p.pillar_topic,
                s.subtopic_title,
                s.target_keyword,
                s.search_volume,
                s.keyword_difficulty,
                s.is_quick_win,
                s.recommended_format,
                s.fix_effort
            FROM geek_seo.niche_subtopics s
            JOIN geek_seo.niche_pillars p ON s.pillar_id = p.id
            WHERE p.niche_profile_id = @ProfileId
              AND s.coverage_status = 'gap'
              AND (@QuickWinsOnly = FALSE OR s.is_quick_win = TRUE)
            ORDER BY s.keyword_difficulty ASC, s.search_volume DESC
            """;

        var rows = await conn.QueryAsync<TopicalGapSummary>(sql,
            new { ProfileId = profileId, QuickWinsOnly = quickWinsOnly });

        return Result<IReadOnlyList<TopicalGapSummary>>.Success(rows.ToList());
    }

    public async Task<Result<IReadOnlyList<AuthorityProgressPoint>>> GetAuthorityProgressAsync(
        Guid projectId, int months = 12, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-months);

        var rows = await conn.QueryAsync<AuthorityProgressPoint>("""
            SELECT
                np.analyzed_at                  AS snapshot_date,
                np.topical_authority_score,
                np.pillars_covered,
                (
                    SELECT COUNT(*)
                    FROM geek_seo.niche_subtopics s
                    JOIN geek_seo.niche_pillars pl ON s.pillar_id = pl.id
                    WHERE pl.niche_profile_id = np.id
                      AND s.coverage_status = 'covered'
                )                               AS total_subtopics_covered,
                np.pillars_gap                  AS total_gaps
            FROM geek_seo.niche_profiles np
            WHERE np.project_id = @ProjectId
              AND np.status = 'complete'
              AND np.analyzed_at >= @Cutoff
            ORDER BY np.analyzed_at ASC
            """, new { ProjectId = projectId, Cutoff = cutoff });

        return Result<IReadOnlyList<AuthorityProgressPoint>>.Success(rows.ToList());
    }

    public async Task<Result<IReadOnlyList<CompetitorNicheOverlap>>> GetCompetitorOverlapAsync(
        Guid profileId, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();

        var rows = await conn.QueryAsync<CompetitorNicheOverlap>("""
            SELECT
                c.domain                                            AS competitor_domain,
                c.pillars_ranking                                   AS shared_pillar_count,
                0                                                   AS competitor_only_pillar_count,
                GREATEST(p.total_pillars_identified - c.pillars_ranking, 0)
                                                                    AS our_only_pillar_count,
                c.estimated_authority_score
            FROM geek_seo.niche_competitors c
            JOIN geek_seo.niche_profiles p ON c.niche_profile_id = p.id
            WHERE c.niche_profile_id = @ProfileId
            ORDER BY c.serp_presence DESC
            """, new { ProfileId = profileId });

        return Result<IReadOnlyList<CompetitorNicheOverlap>>.Success(rows.ToList());
    }

    public async Task<Result<IReadOnlyList<EntityCoverageReport>>> GetEntityCoverageAsync(
        Guid profileId, CancellationToken ct = default)
    {
        using var conn = db.CreateConnection();

        var rows = await conn.QueryAsync<EntityCoverageReport>("""
            SELECT
                entity_name,
                entity_type,
                mention_frequency,
                present_on_domain,
                COALESCE(array_length(associated_pillar_ids, 1), 0) AS associated_pillar_count
            FROM geek_seo.niche_entities
            WHERE niche_profile_id = @ProfileId
            ORDER BY mention_frequency DESC
            """, new { ProfileId = profileId });

        return Result<IReadOnlyList<EntityCoverageReport>>.Success(rows.ToList());
    }
}
