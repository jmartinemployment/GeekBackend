using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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
        var profile = await db.NicheProfiles
            .AsNoTracking()
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.Subtopics)
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.ExistingPages)
            .Include(p => p.Competitors)
            .Include(p => p.Entities)
            .FirstOrDefaultAsync(p => p.Id == profileId, ct);

        if (profile is not null)
        {
            foreach (var pillar in profile.Pillars)
            {
                pillar.NicheProfile = null!;
                foreach (var sub in pillar.Subtopics) sub.Pillar = null!;
                foreach (var page in pillar.ExistingPages) page.Pillar = null!;
            }
            foreach (var c in profile.Competitors) c.NicheProfile = null!;
            foreach (var e in profile.Entities) e.NicheProfile = null!;
        }

        return Result<NicheProfile?>.Success(profile);
    }

    public async Task<Result<NicheProfile?>> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return Result<NicheProfile?>.Success(profile);
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
}
