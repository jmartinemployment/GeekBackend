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
using Microsoft.Extensions.Logging;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteAnalysisProfileRepository(SeoDbContext db, ILogger<SiteAnalysisProfileRepository> logger) : ISiteAnalysisProfileRepository
{
    public async Task<Result<SiteAnalysisProfile>> CreateAsync(SiteAnalysisProfile profile, CancellationToken ct = default)
    {
        if (profile.Id == Guid.Empty)
            profile.Id = Guid.NewGuid();

        db.SiteAnalysisProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return Result<SiteAnalysisProfile>.Success(profile);
    }

    public async Task<Result<SiteAnalysisProfile?>> GetByIdAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = await ProfileWithGraph()
            .FirstOrDefaultAsync(p => p.Id == profileId, ct);

        ClearNavigationCycles(profile);
        return Result<SiteAnalysisProfile?>.Success(profile);
    }

    public async Task<Result<Guid?>> GetProjectIdAsync(Guid profileId, CancellationToken ct = default)
    {
        var projectId = await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => (Guid?)p.ProjectId)
            .FirstOrDefaultAsync(ct);
        return Result<Guid?>.Success(projectId);
    }

    public async Task<Result<SiteAnalysisProfileStatusRow?>> GetStatusRowAsync(
        Guid profileId, CancellationToken ct = default)
    {
        var row = await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new SiteAnalysisProfileStatusRow(
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

        return Result<SiteAnalysisProfileStatusRow?>.Success(row);
    }

    public async Task<Result<SiteAnalysisDetailsRow?>> GetAnalysisDetailsRowAsync(
        Guid profileId, bool includeFusion, CancellationToken ct = default)
    {
        if (!includeFusion)
        {
            var row = await db.SiteAnalysisProfiles.AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => new SiteAnalysisDetailsRow(
                    p.Status,
                    p.AnalysisStepLogVersion,
                    p.AnalysisStepLog,
                    null))
                .FirstOrDefaultAsync(ct);
            return Result<SiteAnalysisDetailsRow?>.Success(row);
        }

        var withFusion = await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new SiteAnalysisDetailsRow(
                p.Status,
                p.AnalysisStepLogVersion,
                p.AnalysisStepLog,
                p.FusionSnapshot))
            .FirstOrDefaultAsync(ct);

        return Result<SiteAnalysisDetailsRow?>.Success(withFusion);
    }

    public async Task<Result<SiteAnalysisProfile?>> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        // Prefer the latest *completed* run so a newer failed/queued re-analyze does not hide pillars.
        var completeId = await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.Status == "complete")
            .OrderByDescending(p => p.AnalyzedAt ?? p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (completeId is not null)
        {
            var profile = await ProfileWithGraph()
                .Where(p => p.Id == completeId.Value)
                .FirstOrDefaultAsync(ct);

            if (profile is not null)
                StripHeavyJsonFields(profile);

            ClearNavigationCycles(profile);
            return Result<SiteAnalysisProfile?>.Success(profile);
        }

        // No complete profile — scalar-only load (avoids JSONB blobs + empty graph during polling).
        var fallbackId = await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (fallbackId is null)
            return Result<SiteAnalysisProfile?>.Success(null);

        var fallback = await LoadProfileScalarsOnly(fallbackId.Value, ct);
        ClearNavigationCycles(fallback);
        return Result<SiteAnalysisProfile?>.Success(fallback);
    }

    private async Task<SiteAnalysisProfile?> LoadProfileScalarsOnly(Guid profileId, CancellationToken ct)
    {
        return await db.SiteAnalysisProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new SiteAnalysisProfile
            {
                Id = p.Id,
                ProjectId = p.ProjectId,
                Domain = p.Domain,
                PrimaryFocus = p.PrimaryFocus,
                FocusDescription = p.FocusDescription,
                FocusTags = p.FocusTags,
                AudienceType = p.AudienceType,
                CompetitionLevel = p.CompetitionLevel,
                DiscoveryMethod = p.DiscoveryMethod,
                TopicalAuthorityScore = p.TopicalAuthorityScore,
                TotalPillarsIdentified = p.TotalPillarsIdentified,
                PillarsCovered = p.PillarsCovered,
                PillarsPartial = p.PillarsPartial,
                PillarsGap = p.PillarsGap,
                AnalyzedAt = p.AnalyzedAt,
                NextAnalysisDue = p.NextAnalysisDue,
                AnalysisVersion = p.AnalysisVersion,
                Status = p.Status,
                AnalysisStep = p.AnalysisStep,
                AnalysisStepNumber = p.AnalysisStepNumber,
                AnalysisTotalSteps = p.AnalysisTotalSteps,
                ErrorMessage = p.ErrorMessage,
                CreatedAt = p.CreatedAt,
                AnalysisProgressAt = p.AnalysisProgressAt,
                AnalysisStepLogVersion = p.AnalysisStepLogVersion,
                StructureStatus = p.StructureStatus,
                EnrichmentStatus = p.EnrichmentStatus,
                ScanFingerprint = p.ScanFingerprint,
                ScanChangeScore = p.ScanChangeScore,
                PersistStage = p.PersistStage,
            })
            .FirstOrDefaultAsync(ct);
    }

    private static void StripHeavyJsonFields(SiteAnalysisProfile profile)
    {
        profile.FusionSnapshot = null;
        profile.AnalysisStepLog = "[]";
        profile.CrawledUrlsJson = null;
        profile.StepStatusesJson = "{}";
    }

    private IQueryable<SiteAnalysisProfile> ProfileWithGraph() =>
        db.SiteAnalysisProfiles
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.Subtopics)
            .Include(p => p.Pillars)
                .ThenInclude(pi => pi.ExistingPages)
            .Include(p => p.Competitors)
            .Include(p => p.Entities);

    private static void ClearNavigationCycles(SiteAnalysisProfile? profile)
    {
        if (profile is null) return;

        foreach (var pillar in profile.Pillars)
        {
            pillar.SiteAnalysisProfile = null;
            foreach (var sub in pillar.Subtopics) sub.Pillar = null;
            foreach (var page in pillar.ExistingPages) page.Pillar = null;
        }
        foreach (var c in profile.Competitors) c.SiteAnalysisProfile = null;
        foreach (var e in profile.Entities) e.SiteAnalysisProfile = null;
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileSummary>>> GetHistoryAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var list = await db.SiteAnalysisProfiles
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new SiteAnalysisProfileSummary(
                p.Id, p.Domain, p.PrimaryFocus,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalysisProfileSummary>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileSummary>>> ListRecentAsync(
        int limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, 200);
        var list = await db.SiteAnalysisProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.AnalyzedAt ?? p.CreatedAt)
            .Take(take)
            .Select(p => new SiteAnalysisProfileSummary(
                p.Id, p.Domain, p.PrimaryFocus,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileSummary>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileSummary>>> ListByNormalizedDomainAsync(
        string normalizedHost, int limit, CancellationToken ct = default)
    {
        var host = (normalizedHost ?? "").Trim().ToLowerInvariant();
        if (host.Length == 0)
            return Result<IReadOnlyList<SiteAnalysisProfileSummary>>.Success([]);

        var take = Math.Clamp(limit, 1, 200);
        // Domain may be stored as bare host or https://host — narrow with Contains then exact-normalize.
        var candidates = await db.SiteAnalysisProfiles
            .AsNoTracking()
            .Where(p => p.Domain.ToLower().Contains(host))
            .OrderByDescending(p => p.AnalyzedAt ?? p.CreatedAt)
            .Take(Math.Max(take * 4, 40))
            .Select(p => new SiteAnalysisProfileSummary(
                p.Id, p.Domain, p.PrimaryFocus,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);

        var matched = candidates
            .Where(p => DomainHostsMatch(NormalizeDomainHost(p.Domain), host))
            .Take(take)
            .ToList();
        return Result<IReadOnlyList<SiteAnalysisProfileSummary>>.Success(matched);
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisPageSectionTreeRow>>> FindTreesByKeywordAsync(
        Guid siteAnalysisProfileId, string keyword, CancellationToken ct = default)
    {
        var needle = (keyword ?? "").Trim();
        if (siteAnalysisProfileId == Guid.Empty || needle.Length == 0)
            return Result<IReadOnlyList<SiteAnalysisPageSectionTreeRow>>.Success([]);

        // Prefer JSON heading shape; also match raw keyword. Escape LIKE wildcards.
        var escaped = EscapeLike(needle);
        var headingNeedle = "%\"HeadingText\":\"%" + escaped + "%";
        var plainNeedle = "%" + escaped + "%";

        var rows = await db.SiteAnalysisPageSectionTrees.AsNoTracking()
            .Where(t => t.SiteAnalysisProfileId == siteAnalysisProfileId
                && (EF.Functions.ILike(t.TreeJson, headingNeedle)
                    || EF.Functions.ILike(t.TreeJson, plainNeedle)))
            .Select(t => new SiteAnalysisPageSectionTreeRow(
                t.Id,
                t.SiteAnalysisProfileId,
                t.PageUrl,
                t.TreeJson,
                t.CreatedAtUtc))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalysisPageSectionTreeRow>>.Success(rows);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string NormalizeDomainHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return raw.Trim().TrimEnd('/').ToLowerInvariant()
                .Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        return host;
    }

    private static bool DomainHostsMatch(string a, string b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public async Task<Result> UpdateStatusAsync(
        Guid profileId, string status, string? step = null,
        int stepNumber = 0, int totalSteps = 0, string? errorMessage = null,
        SiteAnalysisStepLogEntry? stepLogEntry = null,
        CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

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
            profile.AnalysisStepLog = SiteAnalysisStepLogJson.Append(profile.AnalysisStepLog, stepLogEntry);

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
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        profile.TopicalAuthorityScore = authorityScore;
        profile.PillarsCovered = covered;
        profile.PillarsPartial = partial;
        profile.PillarsGap = gap;
        profile.TotalPillarsIdentified = covered + partial + gap;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateProfileSummaryAsync(
        Guid profileId, SiteAnalysisProfileSummaryPatch summary, CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        profile.PrimaryFocus = summary.PrimaryFocus;
        profile.FocusDescription = summary.FocusDescription;
        profile.FocusTags = summary.FocusTags;
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
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        profile.FusionSnapshot = fusionSnapshotJson;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdatePhaseStatusAsync(
        Guid profileId, SiteAnalysisPhaseStatusPatch patch, CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

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

    public async Task<Result> UpsertStepRunAsync(
        Guid profileId,
        SiteAnalysisProfileStepRunUpsert stepRun,
        CancellationToken ct = default)
    {
        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("site analysis profile not found");

        var row = await db.SiteAnalysisProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.SiteAnalysisProfileId == profileId && x.StepSlug == stepRun.StepSlug,
                ct);

        if (row is null)
        {
            row = new SiteAnalysisProfileStepRun
            {
                SiteAnalysisProfileId = profileId,
                StepNumber = stepRun.StepNumber,
                StepSlug = stepRun.StepSlug,
            };
            db.SiteAnalysisProfileStepRuns.Add(row);
        }

        row.StepNumber = stepRun.StepNumber;
        row.StepSlug = stepRun.StepSlug;
        row.Status = stepRun.Status;
        row.StartedAt = stepRun.StartedAt;
        row.HeartbeatAt = stepRun.HeartbeatAt;
        row.CompletedAt = stepRun.CompletedAt;
        row.ErrorMessage = stepRun.ErrorMessage;
        row.RetryCount = stepRun.RetryCount;
        row.InputVersion = stepRun.InputVersion;
        row.OutputVersion = stepRun.OutputVersion;
        row.Summary = stepRun.Summary;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateStepRunStatusAsync(
        Guid profileId,
        string stepSlug,
        SiteAnalysisProfileStepRunStatusPatch patch,
        CancellationToken ct = default)
    {
        await EnsureStepRunsAsync(profileId, ct);
        var row = await db.SiteAnalysisProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.SiteAnalysisProfileId == profileId && x.StepSlug == stepSlug,
                ct);
        if (row is null)
            return Result.Failure("Step run not found");

        row.Status = patch.Status;
        row.HeartbeatAt = patch.HeartbeatAt ?? row.HeartbeatAt;
        row.CompletedAt = patch.CompletedAt ?? row.CompletedAt;
        row.ErrorMessage = patch.ErrorMessage;
        row.Summary = patch.Summary ?? row.Summary;
        if (patch.RetryCount is int retryCount)
            row.RetryCount = retryCount;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileStepRunRow>>> GetStepRunsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisProfileStepRuns.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.StepNumber)
            .Select(x => new SiteAnalysisProfileStepRunRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.StepNumber,
                x.StepSlug,
                x.Status,
                x.StartedAt,
                x.HeartbeatAt,
                x.CompletedAt,
                x.ErrorMessage,
                x.RetryCount,
                x.InputVersion,
                x.OutputVersion,
                x.Summary))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalysisProfileStepRunRow>>.Success(rows);
    }

    public async Task<Result> ReplaceSchemaSignalsAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisProfileSchemaSignalWrite> signals,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisProfileSchemaSignals
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisProfileSchemaSignals.RemoveRange(existing);
        db.SiteAnalysisProfileSchemaSignals.AddRange(signals.Select(x => new SiteAnalysisProfileSchemaSignal
        {
            SiteAnalysisProfileId = profileId,
            SchemaType = x.SchemaType,
            PropertyName = x.PropertyName,
            PropertyValue = x.PropertyValue,
            SourceUrl = x.SourceUrl,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileSchemaSignalRow>>> GetSchemaSignalsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisProfileSchemaSignals.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfileSchemaSignalRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.SchemaType,
                x.PropertyName,
                x.PropertyValue,
                x.SourceUrl,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileSchemaSignalRow>>.Success(rows);
    }

    public async Task<Result> ReplaceDiscoveredUrlsAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisProfileDiscoveredUrlWrite> urls,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisProfileDiscoveredUrls
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisProfileDiscoveredUrls.RemoveRange(existing);
        db.SiteAnalysisProfileDiscoveredUrls.AddRange(urls.Select(x => new SiteAnalysisProfileDiscoveredUrl
        {
            SiteAnalysisProfileId = profileId,
            Url = x.Url,
            SourceType = x.SourceType,
            LastSeenAt = x.LastSeenAt,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileDiscoveredUrlRow>>> GetDiscoveredUrlsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisProfileDiscoveredUrls.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.Url)
            .Select(x => new SiteAnalysisProfileDiscoveredUrlRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.Url,
                x.SourceType,
                x.LastSeenAt))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileDiscoveredUrlRow>>.Success(rows);
    }

    public async Task<Result> ReplaceNavigationLinksAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisProfileNavigationLinkWrite> links,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisProfileNavigationLinks
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisProfileNavigationLinks.RemoveRange(existing);
        db.SiteAnalysisProfileNavigationLinks.AddRange(links.Select(x => new SiteAnalysisProfileNavigationLink
        {
            SiteAnalysisProfileId = profileId,
            SourceUrl = x.SourceUrl,
            LinkUrl = x.LinkUrl,
            AnchorText = x.AnchorText,
            LinkArea = x.LinkArea,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileNavigationLinkRow>>> GetNavigationLinksAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisProfileNavigationLinks.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfileNavigationLinkRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.SourceUrl,
                x.LinkUrl,
                x.AnchorText,
                x.LinkArea,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileNavigationLinkRow>>.Success(rows);
    }

    public async Task<Result> ReplaceHeadingsAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisProfileHeadingWrite> headings,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisProfileHeadings
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisProfileHeadings.RemoveRange(existing);
        db.SiteAnalysisProfileHeadings.AddRange(headings.Select(x => new SiteAnalysisProfileHeading
        {
            SiteAnalysisProfileId = profileId,
            PageUrl = x.PageUrl,
            HeadingLevel = x.HeadingLevel,
            HeadingText = x.HeadingText,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileHeadingRow>>> GetHeadingsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisProfileHeadings.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfileHeadingRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.PageUrl,
                x.HeadingLevel,
                x.HeadingText,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileHeadingRow>>.Success(rows);
    }

    public async Task<Result> ReplacePageSectionTreesAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisPageSectionTreeWrite> pages,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisPageSectionTrees
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisPageSectionTrees.RemoveRange(existing);
        db.SiteAnalysisPageSectionTrees.AddRange(pages.Select(x => new SiteAnalysisPageSectionTree
        {
            SiteAnalysisProfileId = profileId,
            PageUrl = x.PageUrl,
            TreeJson = x.TreeJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisPageSectionTreeRow>>> GetPageSectionTreesAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisPageSectionTrees.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .Select(x => new SiteAnalysisPageSectionTreeRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.PageUrl,
                x.TreeJson,
                x.CreatedAtUtc))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisPageSectionTreeRow>>.Success(rows);
    }

    public async Task<Result> ReplaceExtractedToolsAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisProfileExtractedToolWrite> tools,
        CancellationToken ct = default)
    {
        var existing = await db.ExtractedTools
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.ExtractedTools.RemoveRange(existing);
        db.ExtractedTools.AddRange(tools.Select(x => new SiteAnalysisProfileExtractedTool
        {
            SiteAnalysisProfileId = profileId,
            SitePageId = x.SitePageId,
            Name = x.Name,
            Href = x.Href,
            Department = x.Department,
            Body = x.Body,
            ExtractedAt = DateTimeOffset.UtcNow,
        }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save extracted tools for profile {ProfileId} ({ToolCount} tools)", profileId, tools.Count);
            return Result.Failure(ex.InnerException?.Message ?? ex.Message);
        }
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileExtractedToolRow>>> GetExtractedToolsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.ExtractedTools.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .Select(x => new SiteAnalysisProfileExtractedToolRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.SitePageId,
                x.Name,
                x.Href,
                x.Department,
                x.Body,
                x.ExtractedAt))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisProfileExtractedToolRow>>.Success(rows);
    }

    public async Task<Result> ReplaceTopicCandidateEvidenceAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisTopicCandidateEvidenceWrite> evidence,
        CancellationToken ct = default)
    {
        var candidateIds = await db.SiteAnalysisTopicCandidates.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var existing = await db.SiteAnalysisTopicCandidateEvidenceRows
            .Where(x => candidateIds.Contains(x.TopicCandidateId))
            .ToListAsync(ct);
        db.SiteAnalysisTopicCandidateEvidenceRows.RemoveRange(existing);

        db.SiteAnalysisTopicCandidateEvidenceRows.AddRange(evidence.Select(x => new SiteAnalysisTopicCandidateEvidence
        {
            TopicCandidateId = x.TopicCandidateId,
            EvidenceType = x.EvidenceType,
            SourceUrl = x.SourceUrl,
            SourceLabel = x.SourceLabel,
            EvidenceText = x.EvidenceText,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisTopicCandidateEvidenceRow>>> GetTopicCandidateEvidenceAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.SiteAnalysisTopicCandidateEvidenceRows.AsNoTracking()
            .Where(x => x.TopicCandidate!.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisTopicCandidateEvidenceRow(
                x.Id,
                x.TopicCandidateId,
                x.EvidenceType,
                x.SourceUrl,
                x.SourceLabel,
                x.EvidenceText,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<SiteAnalysisTopicCandidateEvidenceRow>>.Success(rows);
    }

    public async Task<Result> ReplacePageContentAsync(
        Guid profileId,
        SiteAnalysisProfilePageContentWrite content,
        CancellationToken ct = default)
    {
        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("site analysis profile not found");

        var existingItems = await db.SiteAnalysisProfilePageContentItems
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        db.SiteAnalysisProfilePageContentItems.RemoveRange(existingItems);

        db.SiteAnalysisProfilePageContentItems.AddRange(content.Items.Select(x => new SiteAnalysisProfilePageContentItem
        {
            SiteAnalysisProfileId = profileId,
            PageUrl = x.PageUrl,
            ItemKind = x.ItemKind,
            ItemText = x.ItemText,
            DisplayOrder = x.DisplayOrder,
        }));

        var meta = await db.SiteAnalysisProfilePageContentMetaRows
            .FirstOrDefaultAsync(x => x.SiteAnalysisProfileId == profileId, ct);
        if (meta is null)
        {
            db.SiteAnalysisProfilePageContentMetaRows.Add(new SiteAnalysisProfilePageContentMeta
            {
                SiteAnalysisProfileId = profileId,
                PageUrl = content.PageUrl,
                ListItemsScanned = content.ListItemsScanned,
            });
        }
        else
        {
            meta.PageUrl = content.PageUrl;
            meta.ListItemsScanned = content.ListItemsScanned;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<SiteAnalysisProfilePageContentRow?>> GetPageContentAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var meta = await db.SiteAnalysisProfilePageContentMetaRows.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SiteAnalysisProfileId == profileId, ct);
        if (meta is null)
            return Result<SiteAnalysisProfilePageContentRow?>.Success(null);

        var items = await db.SiteAnalysisProfilePageContentItems.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfilePageContentItemRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.PageUrl,
                x.ItemKind,
                x.ItemText,
                x.DisplayOrder))
            .ToListAsync(ct);

        return Result<SiteAnalysisProfilePageContentRow?>.Success(
            new SiteAnalysisProfilePageContentRow(meta.PageUrl, meta.ListItemsScanned, items));
    }

    public async Task<Result> ReplaceSiteStructureAsync(
        Guid profileId,
        SiteAnalysisProfileSiteStructureWrite structure,
        CancellationToken ct = default)
    {
        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("site analysis profile not found");

        var existingPages = await db.SiteAnalysisProfileSitePages
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .ToListAsync(ct);
        var existingByUrl = existingPages.ToDictionary(x => x.Url, StringComparer.OrdinalIgnoreCase);
        var incomingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var incoming in structure.Pages)
        {
            incomingUrls.Add(incoming.Url);
            if (!existingByUrl.TryGetValue(incoming.Url, out var stored))
            {
                stored = new SiteAnalysisProfileSitePage { SiteAnalysisProfileId = profileId };
                db.SiteAnalysisProfileSitePages.Add(stored);
                existingByUrl[incoming.Url] = stored;
                ApplyCrawlDocument(stored, incoming);
                continue;
            }

            ApplyCrawlRecord(stored, incoming);
            if (CrawlDocumentHasher.ShouldReplaceDocument(
                    structure.ForceDocumentWrite,
                    stored.ContentHash,
                    incoming.ContentHash))
            {
                ApplyCrawlDocument(stored, incoming);
            }
        }

        db.SiteAnalysisProfileSitePages.RemoveRange(
            existingPages.Where(x => !incomingUrls.Contains(x.Url)));

        var existingLinks = await db.SiteAnalysisProfileSitePageLinks.Where(x => x.SiteAnalysisProfileId == profileId).ToListAsync(ct);
        db.SiteAnalysisProfileSitePageLinks.RemoveRange(existingLinks);
        db.SiteAnalysisProfileSitePageLinks.AddRange(structure.Links.Select(x => new SiteAnalysisProfileSitePageLink
        {
            SiteAnalysisProfileId = profileId,
            SourceUrl = x.SourceUrl,
            TargetUrl = x.TargetUrl,
            AnchorText = x.AnchorText,
            InferredFromUrlSlug = x.InferredFromUrlSlug,
            DisplayOrder = x.DisplayOrder,
        }));

        var existingPatterns = await db.SiteAnalysisProfileUrlPatternTopics.Where(x => x.SiteAnalysisProfileId == profileId).ToListAsync(ct);
        db.SiteAnalysisProfileUrlPatternTopics.RemoveRange(existingPatterns);
        db.SiteAnalysisProfileUrlPatternTopics.AddRange(structure.UrlPatterns.Select(x => new SiteAnalysisProfileUrlPatternTopic
        {
            SiteAnalysisProfileId = profileId,
            Name = x.Name,
            Slug = x.Slug,
            Url = x.Url,
            PathSegment = x.PathSegment,
            DisplayOrder = x.DisplayOrder,
        }));

        var crawlMeta = await db.SiteAnalysisProfileSiteCrawlMetaRows
            .FirstOrDefaultAsync(x => x.SiteAnalysisProfileId == profileId, ct);
        if (crawlMeta is null)
        {
            db.SiteAnalysisProfileSiteCrawlMetaRows.Add(new SiteAnalysisProfileSiteCrawlMeta
            {
                SiteAnalysisProfileId = profileId,
                PagesAttempted = structure.CrawlMeta.PagesAttempted,
                PagesFetched = structure.CrawlMeta.PagesFetched,
            });
        }
        else
        {
            crawlMeta.PagesAttempted = structure.CrawlMeta.PagesAttempted;
            crawlMeta.PagesFetched = structure.CrawlMeta.PagesFetched;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<SiteAnalysisProfileSiteStructureRow?>> GetSiteStructureAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var pageEntities = await db.SiteAnalysisProfileSitePages.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(ct);
        var pages = pageEntities
            .Select(x => new SiteAnalysisProfileSitePageRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.Url,
                x.FetchMethod,
                x.VisibleText,
                x.WordCount,
                x.DisplayOrder,
                DeserializeContext(x.ContextJson, x.Id),
                x.ContentHash,
                x.FinalUrl,
                x.StatusCode,
                x.Canonical,
                x.NoIndex,
                x.NoFollow,
                x.RedirectChainJson,
                x.FetchedAt))
            .ToList();
        if (pages.Count == 0)
            return Result<SiteAnalysisProfileSiteStructureRow?>.Success(null);

        var links = await db.SiteAnalysisProfileSitePageLinks.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfileSitePageLinkRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.SourceUrl,
                x.TargetUrl,
                x.AnchorText,
                x.InferredFromUrlSlug,
                x.DisplayOrder))
            .ToListAsync(ct);

        var patterns = await db.SiteAnalysisProfileUrlPatternTopics.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new SiteAnalysisProfileUrlPatternTopicRow(
                x.Id,
                x.SiteAnalysisProfileId,
                x.Name,
                x.Slug,
                x.Url,
                x.PathSegment,
                x.DisplayOrder))
            .ToListAsync(ct);

        var crawlMeta = await db.SiteAnalysisProfileSiteCrawlMetaRows.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SiteAnalysisProfileId == profileId, ct);

        return Result<SiteAnalysisProfileSiteStructureRow?>.Success(new SiteAnalysisProfileSiteStructureRow(
            pages,
            links,
            patterns,
            crawlMeta is null
                ? null
                : new SiteAnalysisProfileSiteCrawlMetaRow(
                    crawlMeta.SiteAnalysisProfileId,
                    crawlMeta.PagesAttempted,
                    crawlMeta.PagesFetched)));
    }

    public async Task<Result> BulkUpsertTopicCandidatesAsync(
        Guid profileId,
        IReadOnlyList<SiteAnalysisTopicCandidateBulkUpsert> candidates,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return Result.Success();

        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("site analysis profile not found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO geek_seo.site_analysis_topic_candidates (
                    "Id", "SiteAnalysisProfileId", "Slug", "Name", "Confidence", "IsSelected",
                    "ExclusionReason", "DedicatedPageUrl", "InternalLinkCount", "ContentDepthScore",
                    "DisplayOrder", "EvidenceJson", "CreatedAt"
                ) VALUES (
                    COALESCE(@Id, gen_random_uuid()), @SiteAnalysisProfileId, @Slug, @Name, @Confidence, @IsSelected,
                    @ExclusionReason, @DedicatedPageUrl, @InternalLinkCount, @ContentDepthScore,
                    @DisplayOrder, CAST(@EvidenceJson AS jsonb), @CreatedAt
                )
                ON CONFLICT ("SiteAnalysisProfileId", "Slug") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Confidence" = EXCLUDED."Confidence",
                    "IsSelected" = EXCLUDED."IsSelected",
                    "ExclusionReason" = EXCLUDED."ExclusionReason",
                    "DedicatedPageUrl" = EXCLUDED."DedicatedPageUrl",
                    "InternalLinkCount" = EXCLUDED."InternalLinkCount",
                    "ContentDepthScore" = EXCLUDED."ContentDepthScore",
                    "DisplayOrder" = EXCLUDED."DisplayOrder",
                    "EvidenceJson" = COALESCE(EXCLUDED."EvidenceJson", geek_seo.site_analysis_topic_candidates."EvidenceJson")
                """;

            var now = DateTimeOffset.UtcNow;
            foreach (var c in candidates)
            {
                await conn.ExecuteAsync(sql, new
                {
                    Id = c.Id,
                    SiteAnalysisProfileId = profileId,
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

    public async Task<Result<SiteAnalysisTopicCandidateListResult>> GetTopicCandidatesAsync(
        Guid profileId,
        int page,
        int pageSize,
        bool? selectedOnly,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 5000);

        var query = db.SiteAnalysisTopicCandidates.AsNoTracking()
            .Where(c => c.SiteAnalysisProfileId == profileId);

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
        var candidateIds = rows.Select(x => x.Id).ToList();
        var evidenceRows = new List<(Guid TopicCandidateId, TopicEvidence Evidence)>();
        if (candidateIds.Count > 0)
        {
            var rowsWithEvidence = await db.SiteAnalysisTopicCandidateEvidenceRows.AsNoTracking()
                .Where(x => candidateIds.Contains(x.TopicCandidateId))
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new
                {
                    x.TopicCandidateId,
                    x.EvidenceType,
                    x.SourceUrl,
                    x.EvidenceText,
                })
                .ToListAsync(ct);

            evidenceRows.AddRange(rowsWithEvidence.Select(x => (
                x.TopicCandidateId,
                new TopicEvidence
                {
                    Source = x.EvidenceType,
                    Url = x.SourceUrl,
                    Snippet = x.EvidenceText,
                    Weight = 0,
                })));
        }

        var evidenceByCandidateId = evidenceRows
            .GroupBy(x => x.TopicCandidateId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TopicEvidence>)g.Select(x => x.Evidence).ToList());

        var items = rows
            .Select(row => MapCandidatePage(
                row,
                evidenceByCandidateId.GetValueOrDefault(row.Id)))
            .ToList();
        return Result<SiteAnalysisTopicCandidateListResult>.Success(
            new SiteAnalysisTopicCandidateListResult(items, total, page, pageSize));
    }

    private static SiteAnalysisTopicCandidatePage MapCandidatePage(
        SiteAnalysisTopicCandidate row,
        IReadOnlyList<TopicEvidence>? evidence)
    {
        if ((evidence is null || evidence.Count == 0) && !string.IsNullOrWhiteSpace(row.EvidenceJson))
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

        return new SiteAnalysisTopicCandidatePage(
            row.Id,
            row.SiteAnalysisProfileId,
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
        Guid profileId, SiteAnalysisSaveRequest results, CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        profile.PrimaryFocus = results.PrimaryFocus;
        profile.FocusDescription = results.FocusDescription;
        profile.FocusTags = results.FocusTags;
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
        IEnumerable<SiteAnalysisPillar> pillars, CancellationToken ct = default)
    {
        var list = pillars.ToList();
        if (list.Count == 0) return Result.Success();

        var profileId = list[0].SiteAnalysisProfileId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Re-Analyze reuses pillar Ids; clear the profile's prior pillars first (cascades old
        // subtopics + pillar-pages) so the re-insert can't collide on PK_niche_pillars.
        await db.SiteAnalysisPillars
            .Where(p => p.SiteAnalysisProfileId == profileId)
            .ExecuteDeleteAsync(ct);
        foreach (var p in list)
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
        db.SiteAnalysisPillars.AddRange(list);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }

    // Relies on BulkInsertPillarsAsync cascade-deleting old subtopics + pillars-first call order.
    public async Task<Result> BulkInsertSubtopicsAsync(
        IEnumerable<SiteAnalysisSubtopic> subtopics, CancellationToken ct = default)
    {
        foreach (var s in subtopics)
        {
            if (s.Id == Guid.Empty) s.Id = Guid.NewGuid();
        }
        db.SiteAnalysisSubtopics.AddRange(subtopics);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertCompetitorsAsync(
        IEnumerable<SiteAnalysisCompetitor> competitors, CancellationToken ct = default)
    {
        var list = competitors.ToList();
        if (list.Count == 0) return Result.Success();

        var profileId = list[0].SiteAnalysisProfileId;
        await db.SiteAnalysisCompetitors
            .Where(c => c.SiteAnalysisProfileId == profileId)
            .ExecuteDeleteAsync(ct);

        foreach (var c in list)
        {
            if (c.Id == Guid.Empty) c.Id = Guid.NewGuid();
        }
        db.SiteAnalysisCompetitors.AddRange(list);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisCompetitor>>> GetCompetitorsAsync(
        Guid profileId, CancellationToken ct = default)
    {
        var list = await db.SiteAnalysisCompetitors.AsNoTracking()
            .Where(c => c.SiteAnalysisProfileId == profileId)
            .OrderByDescending(c => c.SerpPresence)
            .ThenBy(c => c.Domain)
            .ToListAsync(ct);

        var deduped = list
            .GroupBy(c => c.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return Result<IReadOnlyList<SiteAnalysisCompetitor>>.Success(deduped);
    }

    public async Task<Result> UpdateCompetitorInsightsAsync(
        SiteAnalysisCompetitor competitor,
        CancellationToken ct = default)
    {
        var existing = await db.SiteAnalysisCompetitors.FirstOrDefaultAsync(x => x.Id == competitor.Id, ct);
        if (existing is null)
            return Result.Failure("Competitor not found");

        db.Entry(existing).CurrentValues.SetValues(competitor);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertEntitiesAsync(
        IEnumerable<SiteAnalysisEntity> entities, CancellationToken ct = default)
    {
        foreach (var e in entities)
        {
            if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
        }
        db.SiteAnalysisEntities.AddRange(entities);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkInsertPillarPagesAsync(
        IEnumerable<SiteAnalysisPillarPage> pages, CancellationToken ct = default)
    {
        foreach (var p in pages)
        {
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
        }
        db.SiteAnalysisPillarPages.AddRange(pages);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisProfileSummary>>> ListDueForReanalysisAsync(
        int limit, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow;
        var list = await db.SiteAnalysisProfiles
            .AsNoTracking()
            .Where(p => p.Status == "complete"
                && p.NextAnalysisDue != null
                && p.NextAnalysisDue <= cutoff)
            .OrderBy(p => p.NextAnalysisDue)
            .Take(limit)
            .Select(p => new SiteAnalysisProfileSummary(
                p.Id, p.Domain, p.PrimaryFocus,
                p.TopicalAuthorityScore, p.TotalPillarsIdentified,
                p.PillarsCovered, p.PillarsGap,
                p.CompetitionLevel, p.AnalyzedAt, p.Status))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalysisProfileSummary>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<SiteAnalysisQueuedJob>>> ListQueuedAsync(
        int limit, CancellationToken ct = default)
    {
        var list = await (
            from profile in db.SiteAnalysisProfiles.AsNoTracking()
            join project in db.Projects.AsNoTracking() on profile.ProjectId equals project.Id
            where profile.Status == "queued"
            orderby profile.CreatedAt
            select new SiteAnalysisQueuedJob(profile.Id, profile.ProjectId, project.UserId, profile.Domain))
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SiteAnalysisQueuedJob>>.Success(list);
    }

    public async Task<Result<int>> FailStaleProcessingAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var stale = await db.SiteAnalysisProfiles
            .Where(p => p.Status == "processing"
                && (p.AnalysisProgressAt ?? p.CreatedAt) < cutoff)
            .ToListAsync(ct);

        foreach (var profile in stale)
        {
            var hasRunningStep = await db.SiteAnalysisProfileStepRuns
                .AnyAsync(
                    x => x.SiteAnalysisProfileId == profile.Id && x.Status == "running",
                    ct);

            if (hasRunningStep)
                continue;

            // Manual step re-runs can finish successfully but leave the profile in processing;
            // heal instead of marking failed when the current step row is already complete.
            if (!string.IsNullOrWhiteSpace(profile.AnalysisStep))
            {
                var currentStepComplete = await db.SiteAnalysisProfileStepRuns
                    .AnyAsync(
                        x => x.SiteAnalysisProfileId == profile.Id
                            && x.StepSlug == profile.AnalysisStep
                            && x.Status == "complete",
                        ct);

                if (currentStepComplete)
                {
                    profile.Status = "pending";
                    profile.ErrorMessage = null;
                    continue;
                }
            }

            profile.Status = "failed";
            profile.ErrorMessage =
                "Analysis timed out or was interrupted (often during navigation crawl). Click Re-analyze to run again.";
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);

        return Result<int>.Success(stale.Count);
    }

    public async Task<Result> UpdateStepStatusAsync(
        Guid profileId,
        string slug,
        string status,
        SiteAnalysisStepLogEntry? entry = null,
        CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var row = await db.SiteAnalysisProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.SiteAnalysisProfileId == profileId && x.StepSlug == slug,
                ct);
        if (row is null)
            return Result.Failure($"Step run not found for slug '{slug}'.");

        var now = DateTimeOffset.UtcNow;
        row.Status = status;
        row.HeartbeatAt = now;

        if (status == "running" && row.StartedAt is null)
            row.StartedAt = now;

        if (status is "complete" or "skipped")
        {
            row.CompletedAt = now;
            row.ErrorMessage = null;
            if (!string.IsNullOrWhiteSpace(entry?.Summary))
                row.Summary = entry.Summary;
        }
        else if (status == "error")
        {
            row.ErrorMessage = entry?.Summary ?? "Step failed.";
        }

        if (entry is not null)
            profile.AnalysisStepLog = SiteAnalysisStepLogJson.Append(profile.AnalysisStepLog, entry);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> InvalidateDownstreamStepsAsync(
        Guid profileId,
        IReadOnlyList<string> downstreamSlugs,
        CancellationToken ct = default)
    {
        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("site analysis profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var slugSet = downstreamSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await db.SiteAnalysisProfileStepRuns
            .Where(x => x.SiteAnalysisProfileId == profileId && slugSet.Contains(x.StepSlug))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.Status = "pending";
            row.StartedAt = null;
            row.HeartbeatAt = null;
            row.CompletedAt = null;
            row.ErrorMessage = null;
            row.Summary = null;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateCrawledUrlsAsync(
        Guid profileId,
        string crawledUrlsJson,
        CancellationToken ct = default)
    {
        var profile = await db.SiteAnalysisProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("site analysis profile not found");

        profile.CrawledUrlsJson = crawledUrlsJson;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetStepStatusesAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var profileExists = await db.SiteAnalysisProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result<IReadOnlyDictionary<string, string>>.Failure("site analysis profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var rows = await db.SiteAnalysisProfileStepRuns.AsNoTracking()
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .Select(x => new { x.StepSlug, x.Status })
            .ToListAsync(ct);

        var statuses = rows.ToDictionary(
            x => x.StepSlug,
            x => x.Status,
            StringComparer.OrdinalIgnoreCase);
        return Result<IReadOnlyDictionary<string, string>>.Success(statuses);
    }

    private async Task EnsureStepRunsAsync(Guid profileId, CancellationToken ct)
    {
        var existingSlugs = await db.SiteAnalysisProfileStepRuns
            .Where(x => x.SiteAnalysisProfileId == profileId)
            .Select(x => x.StepSlug)
            .ToListAsync(ct);
        var existing = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = SiteAnalysisStepRunDefaults.Ordered
            .Where(step => !existing.Contains(step.StepSlug))
            .ToList();
        if (missing.Count == 0)
            return;

        foreach (var (stepNumber, stepSlug) in missing)
        {
            db.SiteAnalysisProfileStepRuns.Add(new SiteAnalysisProfileStepRun
            {
                SiteAnalysisProfileId = profileId,
                StepNumber = stepNumber,
                StepSlug = stepSlug,
                Status = "pending",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static void ApplyCrawlRecord(SiteAnalysisProfileSitePage stored, SiteAnalysisProfileSitePageWrite incoming)
    {
        stored.Url = incoming.Url;
        stored.FetchMethod = incoming.FetchMethod;
        stored.DisplayOrder = incoming.DisplayOrder;
        stored.FinalUrl = incoming.FinalUrl;
        stored.StatusCode = incoming.StatusCode;
        stored.Canonical = incoming.Canonical;
        stored.NoIndex = incoming.NoIndex;
        stored.NoFollow = incoming.NoFollow;
        stored.RedirectChainJson = incoming.RedirectChainJson;
        stored.FetchedAt = incoming.FetchedAt == default ? DateTimeOffset.UtcNow : incoming.FetchedAt;
    }

    private static void ApplyCrawlDocument(SiteAnalysisProfileSitePage stored, SiteAnalysisProfileSitePageWrite incoming)
    {
        ApplyCrawlRecord(stored, incoming);
        stored.VisibleText = incoming.VisibleText;
        stored.WordCount = incoming.WordCount;
        stored.ContentHash = incoming.ContentHash;
        stored.ContextJson = JsonSerializer.Serialize(incoming.ContextData ?? new PageContext(), ContextJsonOptions);
    }

    private PageContext DeserializeContext(string json, Guid sitePageId)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new PageContext();
        try
        {
            return JsonSerializer.Deserialize<PageContext>(json, ContextJsonOptions) ?? new PageContext();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize ContextJson for site page {SitePageId}; returning empty PageContext", sitePageId);
            return new PageContext();
        }
    }
}
