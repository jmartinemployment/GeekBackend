using System.Data;
using System.Text.Json;
using Dapper;
using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeekRepository.Repositories.Seo;

public sealed class NicheProfileRepository(SeoDbContext db) : INicheProfileRepository
{
    public async Task<Result<NicheProfile>> CreateAsync(NicheProfile profile, CancellationToken ct = default)
    {
        if (profile.Id == Guid.Empty)
            profile.Id = Guid.NewGuid();

        db.NicheProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return Result<NicheProfile>.Success(profile);
    }

    public async Task<Result<NicheProfile?>> GetByIdAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = await ProfileWithGraph()
            .FirstOrDefaultAsync(p => p.Id == profileId, ct);

        ClearNavigationCycles(profile);
        return Result<NicheProfile?>.Success(profile);
    }

    public async Task<Result<NicheProfileStatusRow?>> GetStatusRowAsync(
        Guid profileId, CancellationToken ct = default)
    {
        var row = await db.NicheProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new NicheProfileStatusRow(
                p.Id,
                p.Status,
                p.AnalysisStep,
                p.AnalysisStepNumber,
                p.AnalysisTotalSteps,
                p.ErrorMessage,
                p.CreatedAt,
                p.AnalysisProgressAt,
                p.StructureStatus,
                p.EnrichmentStatus,
                p.PersistStage))
            .FirstOrDefaultAsync(ct);

        return Result<NicheProfileStatusRow?>.Success(row);
    }

    public async Task<Result<NicheAnalysisDetailsRow?>> GetAnalysisDetailsRowAsync(
        Guid profileId, bool includeFusion, CancellationToken ct = default)
    {
        if (!includeFusion)
        {
            var row = await db.NicheProfiles.AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => new NicheAnalysisDetailsRow(
                    p.Status,
                    p.AnalysisStepLogVersion,
                    p.AnalysisStepLog,
                    null))
                .FirstOrDefaultAsync(ct);
            return Result<NicheAnalysisDetailsRow?>.Success(row);
        }

        var withFusion = await db.NicheProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new NicheAnalysisDetailsRow(
                p.Status,
                p.AnalysisStepLogVersion,
                p.AnalysisStepLog,
                p.FusionSnapshot))
            .FirstOrDefaultAsync(ct);

        return Result<NicheAnalysisDetailsRow?>.Success(withFusion);
    }

    public async Task<Result<NicheProfile?>> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        // Prefer the latest *completed* run so a newer failed/queued re-analyze does not hide pillars.
        var profile = await ProfileWithGraph()
            .Where(p => p.ProjectId == projectId && p.Status == "complete")
            .OrderByDescending(p => p.AnalyzedAt ?? p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (profile is null)
        {
            profile = await ProfileWithGraph()
                .Where(p => p.ProjectId == projectId)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        ClearNavigationCycles(profile);
        return Result<NicheProfile?>.Success(profile);
    }

    private IQueryable<NicheProfile> ProfileWithGraph() =>
        db.NicheProfiles
            .AsNoTracking()
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.Subtopics)
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.ExistingPages)
            .Include(p => p.Competitors)
            .Include(p => p.Entities);

    private static void ClearNavigationCycles(NicheProfile? profile)
    {
        if (profile is null) return;

        foreach (var pillar in profile.Pillars)
        {
            pillar.NicheProfile = null;
            foreach (var sub in pillar.Subtopics) sub.Pillar = null;
            foreach (var page in pillar.ExistingPages) page.Pillar = null;
        }
        foreach (var c in profile.Competitors) c.NicheProfile = null;
        foreach (var e in profile.Entities) e.NicheProfile = null;
    }

    public async Task<Result<IReadOnlyList<NicheProfileSummary>>> GetHistoryAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var list = await db.NicheProfiles
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new NicheProfileSummary(
                p.Id, p.Domain, p.PrimaryNiche,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);

        return Result<IReadOnlyList<NicheProfileSummary>>.Success(list);
    }

    public async Task<Result> UpdateStatusAsync(
        Guid profileId, string status, string? step = null,
        int stepNumber = 0, int totalSteps = 0, string? errorMessage = null,
        NicheAnalysisStepLogEntry? stepLogEntry = null,
        CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.Status = status;
        profile.ErrorMessage = errorMessage;
        if (step is not null)
            profile.AnalysisStep = step;
        if (stepNumber > 0)
            profile.AnalysisStepNumber = stepNumber;
        if (totalSteps > 0)
            profile.AnalysisTotalSteps = totalSteps;
        if (status is "processing" or "queued")
            profile.AnalysisProgressAt = DateTimeOffset.UtcNow;

        if (stepLogEntry is not null)
            profile.AnalysisStepLog = NicheAnalysisStepLogJson.Append(profile.AnalysisStepLog, stepLogEntry);

        if (status is "complete")
        {
            profile.AnalyzedAt = DateTimeOffset.UtcNow;
            profile.NextAnalysisDue = DateTimeOffset.UtcNow.AddDays(30);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateScoresAsync(
        Guid profileId, decimal authorityScore, int covered, int partial, int gap,
        CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.TopicalAuthorityScore = authorityScore;
        profile.PillarsCovered = covered;
        profile.PillarsPartial = partial;
        profile.PillarsGap = gap;
        profile.TotalPillarsIdentified = covered + partial + gap;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateProfileSummaryAsync(
        Guid profileId, NicheProfileSummaryPatch summary, CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.PrimaryNiche = summary.PrimaryNiche;
        profile.NicheDescription = summary.NicheDescription;
        profile.NicheTags = summary.NicheTags;
        profile.AudienceType = summary.AudienceType;
        profile.TotalPillarsIdentified = summary.TotalPillarsIdentified;
        profile.AnalyzedAt = summary.AnalyzedAt;
        profile.NextAnalysisDue = summary.NextAnalysisDue;

        if (summary.ScanFingerprint is not null)
            profile.ScanFingerprint = summary.ScanFingerprint;
        if (summary.ScanChangeScore is not null)
            profile.ScanChangeScore = summary.ScanChangeScore;
        if (summary.PersistStage is not null)
            profile.PersistStage = summary.PersistStage;
        if (summary.StructureStatus is not null)
            profile.StructureStatus = summary.StructureStatus;
        if (summary.EnrichmentStatus is not null)
            profile.EnrichmentStatus = summary.EnrichmentStatus;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SaveFusionSnapshotAsync(
        Guid profileId, string fusionSnapshotJson, CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.FusionSnapshot = fusionSnapshotJson;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdatePhaseStatusAsync(
        Guid profileId, NichePhaseStatusPatch patch, CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        if (patch.StructureStatus is not null)
            profile.StructureStatus = patch.StructureStatus;
        if (patch.EnrichmentStatus is not null)
            profile.EnrichmentStatus = patch.EnrichmentStatus;
        if (patch.PersistStage is not null)
            profile.PersistStage = patch.PersistStage;
        if (patch.Status is not null)
        {
            profile.Status = patch.Status;
            if (patch.Status is "complete" && profile.AnalyzedAt is null)
            {
                profile.AnalyzedAt = DateTimeOffset.UtcNow;
                profile.NextAnalysisDue = DateTimeOffset.UtcNow.AddDays(30);
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkUpsertTopicCandidatesAsync(
        Guid profileId,
        IReadOnlyList<NicheTopicCandidateBulkUpsert> candidates,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return Result.Success();

        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("Niche profile not found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO geek_seo.niche_topic_candidates (
                    "Id", "NicheProfileId", "Slug", "Name", "Confidence", "IsSelected",
                    "ExclusionReason", "DedicatedPageUrl", "InternalLinkCount", "ContentDepthScore",
                    "DisplayOrder", "EvidenceJson", "CreatedAt"
                ) VALUES (
                    COALESCE(@Id, gen_random_uuid()), @NicheProfileId, @Slug, @Name, @Confidence, @IsSelected,
                    @ExclusionReason, @DedicatedPageUrl, @InternalLinkCount, @ContentDepthScore,
                    @DisplayOrder, CAST(@EvidenceJson AS jsonb), @CreatedAt
                )
                ON CONFLICT ("NicheProfileId", "Slug") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Confidence" = EXCLUDED."Confidence",
                    "IsSelected" = EXCLUDED."IsSelected",
                    "ExclusionReason" = EXCLUDED."ExclusionReason",
                    "DedicatedPageUrl" = EXCLUDED."DedicatedPageUrl",
                    "InternalLinkCount" = EXCLUDED."InternalLinkCount",
                    "ContentDepthScore" = EXCLUDED."ContentDepthScore",
                    "DisplayOrder" = EXCLUDED."DisplayOrder",
                    "EvidenceJson" = COALESCE(EXCLUDED."EvidenceJson", geek_seo.niche_topic_candidates."EvidenceJson")
                """;

            var now = DateTimeOffset.UtcNow;
            foreach (var c in candidates)
            {
                await conn.ExecuteAsync(sql, new
                {
                    Id = c.Id,
                    NicheProfileId = profileId,
                    c.Slug,
                    c.Name,
                    c.Confidence,
                    c.IsSelected,
                    c.ExclusionReason,
                    c.DedicatedPageUrl,
                    c.InternalLinkCount,
                    c.ContentDepthScore,
                    c.DisplayOrder,
                    c.EvidenceJson,
                    CreatedAt = now,
                }, transaction: tx.GetDbTransaction());
            }

            await tx.CommitAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result<NicheTopicCandidateListResult>> GetTopicCandidatesAsync(
        Guid profileId,
        int page,
        int pageSize,
        bool? selectedOnly,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.NicheTopicCandidates.AsNoTracking()
            .Where(c => c.NicheProfileId == profileId);

        if (selectedOnly == true)
            query = query.Where(c => c.IsSelected);
        else if (selectedOnly == false)
            query = query.Where(c => !c.IsSelected);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(c => c.DisplayOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(MapCandidatePage).ToList();
        return Result<NicheTopicCandidateListResult>.Success(
            new NicheTopicCandidateListResult(items, total, page, pageSize));
    }

    private static NicheTopicCandidatePage MapCandidatePage(NicheTopicCandidate row)
    {
        IReadOnlyList<TopicEvidence>? evidence = null;
        if (!string.IsNullOrWhiteSpace(row.EvidenceJson))
        {
            try
            {
                evidence = JsonSerializer.Deserialize<List<TopicEvidence>>(row.EvidenceJson)
                    ?? [];
            }
            catch
            {
                evidence = [];
            }
        }

        return new NicheTopicCandidatePage(
            row.Id,
            row.NicheProfileId,
            row.Slug,
            row.Name,
            row.Confidence,
            row.IsSelected,
            row.ExclusionReason,
            row.DedicatedPageUrl,
            row.InternalLinkCount,
            row.ContentDepthScore,
            row.DisplayOrder,
            evidence);
    }

    public async Task<Result> SaveAnalysisResultsAsync(
        Guid profileId, NicheAnalysisSaveRequest results, CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.PrimaryNiche = results.PrimaryNiche;
        profile.NicheDescription = results.NicheDescription;
        profile.NicheTags = results.NicheTags;
        profile.AudienceType = results.AudienceType;
        profile.DiscoveryMethod = results.DiscoveryMethod;
        profile.TopicalAuthorityScore = results.AuthorityScore;
        profile.TotalPillarsIdentified = results.TotalPillarsIdentified;
        profile.PillarsCovered = results.Covered;
        profile.PillarsPartial = results.Partial;
        profile.PillarsGap = results.Gap;
        profile.AnalyzedAt = results.AnalyzedAt;
        profile.NextAnalysisDue = results.NextAnalysisDue;
        profile.FusionSnapshot = results.FusionSnapshot;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertPillarsAsync(
        IEnumerable<NichePillar> pillars, CancellationToken ct = default)
    {
        foreach (var p in pillars)
        {
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
        }
        db.NichePillars.AddRange(pillars);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertSubtopicsAsync(
        IEnumerable<NicheSubtopic> subtopics, CancellationToken ct = default)
    {
        foreach (var s in subtopics)
        {
            if (s.Id == Guid.Empty) s.Id = Guid.NewGuid();
        }
        db.NicheSubtopics.AddRange(subtopics);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertCompetitorsAsync(
        IEnumerable<NicheCompetitor> competitors, CancellationToken ct = default)
    {
        foreach (var c in competitors)
        {
            if (c.Id == Guid.Empty) c.Id = Guid.NewGuid();
        }
        db.NicheCompetitors.AddRange(competitors);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertEntitiesAsync(
        IEnumerable<NicheEntity> entities, CancellationToken ct = default)
    {
        foreach (var e in entities)
        {
            if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
        }
        db.NicheEntities.AddRange(entities);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertPillarPagesAsync(
        IEnumerable<NichePillarPage> pages, CancellationToken ct = default)
    {
        foreach (var p in pages)
        {
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
        }
        db.NichePillarPages.AddRange(pages);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheProfileSummary>>> ListDueForReanalysisAsync(
        int limit, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow;
        var list = await db.NicheProfiles
            .AsNoTracking()
            .Where(p => p.Status == "complete"
                && p.NextAnalysisDue != null
                && p.NextAnalysisDue <= cutoff)
            .OrderBy(p => p.NextAnalysisDue)
            .Take(limit)
            .Select(p => new NicheProfileSummary(
                p.Id, p.Domain, p.PrimaryNiche,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);

        return Result<IReadOnlyList<NicheProfileSummary>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<NicheQueuedJob>>> ListQueuedAsync(
        int limit, CancellationToken ct = default)
    {
        var list = await (
            from profile in db.NicheProfiles.AsNoTracking()
            join project in db.Projects.AsNoTracking() on profile.ProjectId equals project.Id
            where profile.Status == "queued"
            orderby profile.CreatedAt
            select new NicheQueuedJob(profile.Id, profile.ProjectId, project.UserId, profile.Domain))
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);

        return Result<IReadOnlyList<NicheQueuedJob>>.Success(list);
    }

    public async Task<Result<int>> FailStaleProcessingAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var stale = await db.NicheProfiles
            .Where(p => p.Status == "processing"
                && (p.AnalysisProgressAt ?? p.CreatedAt) < cutoff)
            .ToListAsync(ct);

        foreach (var profile in stale)
        {
            profile.Status = "failed";
            profile.ErrorMessage =
                "Analysis timed out or was interrupted (often during navigation crawl). Click Re-analyze to run again.";
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);

        return Result<int>.Success(stale.Count);
    }
}
