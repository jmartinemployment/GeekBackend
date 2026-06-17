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

    public async Task<Result<Guid?>> GetProjectIdAsync(Guid profileId, CancellationToken ct = default)
    {
        var projectId = await db.NicheProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => (Guid?)p.ProjectId)
            .FirstOrDefaultAsync(ct);
        return Result<Guid?>.Success(projectId);
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
        var completeId = await db.NicheProfiles.AsNoTracking()
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
            return Result<NicheProfile?>.Success(profile);
        }

        // No complete profile — scalar-only load (avoids JSONB blobs + empty graph during polling).
        var fallbackId = await db.NicheProfiles.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (fallbackId is null)
            return Result<NicheProfile?>.Success(null);

        var fallback = await LoadProfileScalarsOnly(fallbackId.Value, ct);
        ClearNavigationCycles(fallback);
        return Result<NicheProfile?>.Success(fallback);
    }

    private async Task<NicheProfile?> LoadProfileScalarsOnly(Guid profileId, CancellationToken ct)
    {
        return await db.NicheProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new NicheProfile
            {
                Id = p.Id,
                ProjectId = p.ProjectId,
                Domain = p.Domain,
                PrimaryNiche = p.PrimaryNiche,
                NicheDescription = p.NicheDescription,
                NicheTags = p.NicheTags,
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

    private static void StripHeavyJsonFields(NicheProfile profile)
    {
        profile.FusionSnapshot = null;
        profile.AnalysisStepLog = "[]";
        profile.CrawledUrlsJson = null;
        profile.StepStatusesJson = "{}";
    }

    private IQueryable<NicheProfile> ProfileWithGraph() =>
        db.NicheProfiles
            .AsNoTracking()
            .AsSplitQuery()
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

    public async Task<Result> UpsertStepRunAsync(
        Guid profileId,
        NicheProfileStepRunUpsert stepRun,
        CancellationToken ct = default)
    {
        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("Niche profile not found");

        var row = await db.NicheProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.NicheProfileId == profileId && x.StepSlug == stepRun.StepSlug,
                ct);

        if (row is null)
        {
            row = new NicheProfileStepRun
            {
                NicheProfileId = profileId,
                StepNumber = stepRun.StepNumber,
                StepSlug = stepRun.StepSlug,
            };
            db.NicheProfileStepRuns.Add(row);
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
        NicheProfileStepRunStatusPatch patch,
        CancellationToken ct = default)
    {
        await EnsureStepRunsAsync(profileId, ct);
        var row = await db.NicheProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.NicheProfileId == profileId && x.StepSlug == stepSlug,
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

    public async Task<Result<IReadOnlyList<NicheProfileStepRunRow>>> GetStepRunsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheProfileStepRuns.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.StepNumber)
            .Select(x => new NicheProfileStepRunRow(
                x.Id,
                x.NicheProfileId,
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

        return Result<IReadOnlyList<NicheProfileStepRunRow>>.Success(rows);
    }

    public async Task<Result> ReplaceSchemaSignalsAsync(
        Guid profileId,
        IReadOnlyList<NicheProfileSchemaSignalWrite> signals,
        CancellationToken ct = default)
    {
        var existing = await db.NicheProfileSchemaSignals
            .Where(x => x.NicheProfileId == profileId)
            .ToListAsync(ct);
        db.NicheProfileSchemaSignals.RemoveRange(existing);
        db.NicheProfileSchemaSignals.AddRange(signals.Select(x => new NicheProfileSchemaSignal
        {
            NicheProfileId = profileId,
            SchemaType = x.SchemaType,
            PropertyName = x.PropertyName,
            PropertyValue = x.PropertyValue,
            SourceUrl = x.SourceUrl,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheProfileSchemaSignalRow>>> GetSchemaSignalsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheProfileSchemaSignals.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileSchemaSignalRow(
                x.Id,
                x.NicheProfileId,
                x.SchemaType,
                x.PropertyName,
                x.PropertyValue,
                x.SourceUrl,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<NicheProfileSchemaSignalRow>>.Success(rows);
    }

    public async Task<Result> ReplaceDiscoveredUrlsAsync(
        Guid profileId,
        IReadOnlyList<NicheProfileDiscoveredUrlWrite> urls,
        CancellationToken ct = default)
    {
        var existing = await db.NicheProfileDiscoveredUrls
            .Where(x => x.NicheProfileId == profileId)
            .ToListAsync(ct);
        db.NicheProfileDiscoveredUrls.RemoveRange(existing);
        db.NicheProfileDiscoveredUrls.AddRange(urls.Select(x => new NicheProfileDiscoveredUrl
        {
            NicheProfileId = profileId,
            Url = x.Url,
            SourceType = x.SourceType,
            LastSeenAt = x.LastSeenAt,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheProfileDiscoveredUrlRow>>> GetDiscoveredUrlsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheProfileDiscoveredUrls.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.Url)
            .Select(x => new NicheProfileDiscoveredUrlRow(
                x.Id,
                x.NicheProfileId,
                x.Url,
                x.SourceType,
                x.LastSeenAt))
            .ToListAsync(ct);
        return Result<IReadOnlyList<NicheProfileDiscoveredUrlRow>>.Success(rows);
    }

    public async Task<Result> ReplaceNavigationLinksAsync(
        Guid profileId,
        IReadOnlyList<NicheProfileNavigationLinkWrite> links,
        CancellationToken ct = default)
    {
        var existing = await db.NicheProfileNavigationLinks
            .Where(x => x.NicheProfileId == profileId)
            .ToListAsync(ct);
        db.NicheProfileNavigationLinks.RemoveRange(existing);
        db.NicheProfileNavigationLinks.AddRange(links.Select(x => new NicheProfileNavigationLink
        {
            NicheProfileId = profileId,
            SourceUrl = x.SourceUrl,
            LinkUrl = x.LinkUrl,
            AnchorText = x.AnchorText,
            LinkArea = x.LinkArea,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheProfileNavigationLinkRow>>> GetNavigationLinksAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheProfileNavigationLinks.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileNavigationLinkRow(
                x.Id,
                x.NicheProfileId,
                x.SourceUrl,
                x.LinkUrl,
                x.AnchorText,
                x.LinkArea,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<NicheProfileNavigationLinkRow>>.Success(rows);
    }

    public async Task<Result> ReplaceHeadingsAsync(
        Guid profileId,
        IReadOnlyList<NicheProfileHeadingWrite> headings,
        CancellationToken ct = default)
    {
        var existing = await db.NicheProfileHeadings
            .Where(x => x.NicheProfileId == profileId)
            .ToListAsync(ct);
        db.NicheProfileHeadings.RemoveRange(existing);
        db.NicheProfileHeadings.AddRange(headings.Select(x => new NicheProfileHeading
        {
            NicheProfileId = profileId,
            PageUrl = x.PageUrl,
            HeadingLevel = x.HeadingLevel,
            HeadingText = x.HeadingText,
            DisplayOrder = x.DisplayOrder,
        }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheProfileHeadingRow>>> GetHeadingsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheProfileHeadings.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileHeadingRow(
                x.Id,
                x.NicheProfileId,
                x.PageUrl,
                x.HeadingLevel,
                x.HeadingText,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<NicheProfileHeadingRow>>.Success(rows);
    }

    public async Task<Result> ReplaceTopicCandidateEvidenceAsync(
        Guid profileId,
        IReadOnlyList<NicheTopicCandidateEvidenceWrite> evidence,
        CancellationToken ct = default)
    {
        var candidateIds = await db.NicheTopicCandidates.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var existing = await db.NicheTopicCandidateEvidenceRows
            .Where(x => candidateIds.Contains(x.TopicCandidateId))
            .ToListAsync(ct);
        db.NicheTopicCandidateEvidenceRows.RemoveRange(existing);

        db.NicheTopicCandidateEvidenceRows.AddRange(evidence.Select(x => new NicheTopicCandidateEvidence
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

    public async Task<Result<IReadOnlyList<NicheTopicCandidateEvidenceRow>>> GetTopicCandidateEvidenceAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var rows = await db.NicheTopicCandidateEvidenceRows.AsNoTracking()
            .Where(x => x.TopicCandidate!.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheTopicCandidateEvidenceRow(
                x.Id,
                x.TopicCandidateId,
                x.EvidenceType,
                x.SourceUrl,
                x.SourceLabel,
                x.EvidenceText,
                x.DisplayOrder))
            .ToListAsync(ct);
        return Result<IReadOnlyList<NicheTopicCandidateEvidenceRow>>.Success(rows);
    }

    public async Task<Result> ReplacePageContentAsync(
        Guid profileId,
        NicheProfilePageContentWrite content,
        CancellationToken ct = default)
    {
        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("Niche profile not found");

        var existingItems = await db.NicheProfilePageContentItems
            .Where(x => x.NicheProfileId == profileId)
            .ToListAsync(ct);
        db.NicheProfilePageContentItems.RemoveRange(existingItems);

        db.NicheProfilePageContentItems.AddRange(content.Items.Select(x => new NicheProfilePageContentItem
        {
            NicheProfileId = profileId,
            PageUrl = x.PageUrl,
            ItemKind = x.ItemKind,
            ItemText = x.ItemText,
            DisplayOrder = x.DisplayOrder,
        }));

        var meta = await db.NicheProfilePageContentMetaRows
            .FirstOrDefaultAsync(x => x.NicheProfileId == profileId, ct);
        if (meta is null)
        {
            db.NicheProfilePageContentMetaRows.Add(new NicheProfilePageContentMeta
            {
                NicheProfileId = profileId,
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

    public async Task<Result<NicheProfilePageContentRow?>> GetPageContentAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var meta = await db.NicheProfilePageContentMetaRows.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NicheProfileId == profileId, ct);
        if (meta is null)
            return Result<NicheProfilePageContentRow?>.Success(null);

        var items = await db.NicheProfilePageContentItems.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfilePageContentItemRow(
                x.Id,
                x.NicheProfileId,
                x.PageUrl,
                x.ItemKind,
                x.ItemText,
                x.DisplayOrder))
            .ToListAsync(ct);

        return Result<NicheProfilePageContentRow?>.Success(
            new NicheProfilePageContentRow(meta.PageUrl, meta.ListItemsScanned, items));
    }

    public async Task<Result> ReplaceSiteStructureAsync(
        Guid profileId,
        NicheProfileSiteStructureWrite structure,
        CancellationToken ct = default)
    {
        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("Niche profile not found");

        var existingPages = await db.NicheProfileSitePages.Where(x => x.NicheProfileId == profileId).ToListAsync(ct);
        db.NicheProfileSitePages.RemoveRange(existingPages);
        db.NicheProfileSitePages.AddRange(structure.Pages.Select(x => new NicheProfileSitePage
        {
            NicheProfileId = profileId,
            Url = x.Url,
            FetchMethod = x.FetchMethod,
            VisibleText = x.VisibleText,
            WordCount = x.WordCount,
            DisplayOrder = x.DisplayOrder,
        }));

        var existingLinks = await db.NicheProfileSitePageLinks.Where(x => x.NicheProfileId == profileId).ToListAsync(ct);
        db.NicheProfileSitePageLinks.RemoveRange(existingLinks);
        db.NicheProfileSitePageLinks.AddRange(structure.Links.Select(x => new NicheProfileSitePageLink
        {
            NicheProfileId = profileId,
            SourceUrl = x.SourceUrl,
            TargetUrl = x.TargetUrl,
            AnchorText = x.AnchorText,
            InferredFromUrlSlug = x.InferredFromUrlSlug,
            DisplayOrder = x.DisplayOrder,
        }));

        var existingPatterns = await db.NicheProfileUrlPatternTopics.Where(x => x.NicheProfileId == profileId).ToListAsync(ct);
        db.NicheProfileUrlPatternTopics.RemoveRange(existingPatterns);
        db.NicheProfileUrlPatternTopics.AddRange(structure.UrlPatterns.Select(x => new NicheProfileUrlPatternTopic
        {
            NicheProfileId = profileId,
            Name = x.Name,
            Slug = x.Slug,
            Url = x.Url,
            PathSegment = x.PathSegment,
            DisplayOrder = x.DisplayOrder,
        }));

        var crawlMeta = await db.NicheProfileSiteCrawlMetaRows
            .FirstOrDefaultAsync(x => x.NicheProfileId == profileId, ct);
        if (crawlMeta is null)
        {
            db.NicheProfileSiteCrawlMetaRows.Add(new NicheProfileSiteCrawlMeta
            {
                NicheProfileId = profileId,
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

    public async Task<Result<NicheProfileSiteStructureRow?>> GetSiteStructureAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var pages = await db.NicheProfileSitePages.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileSitePageRow(
                x.Id,
                x.NicheProfileId,
                x.Url,
                x.FetchMethod,
                x.VisibleText,
                x.WordCount,
                x.DisplayOrder))
            .ToListAsync(ct);
        if (pages.Count == 0)
            return Result<NicheProfileSiteStructureRow?>.Success(null);

        var links = await db.NicheProfileSitePageLinks.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileSitePageLinkRow(
                x.Id,
                x.NicheProfileId,
                x.SourceUrl,
                x.TargetUrl,
                x.AnchorText,
                x.InferredFromUrlSlug,
                x.DisplayOrder))
            .ToListAsync(ct);

        var patterns = await db.NicheProfileUrlPatternTopics.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new NicheProfileUrlPatternTopicRow(
                x.Id,
                x.NicheProfileId,
                x.Name,
                x.Slug,
                x.Url,
                x.PathSegment,
                x.DisplayOrder))
            .ToListAsync(ct);

        var crawlMeta = await db.NicheProfileSiteCrawlMetaRows.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NicheProfileId == profileId, ct);

        return Result<NicheProfileSiteStructureRow?>.Success(new NicheProfileSiteStructureRow(
            pages,
            links,
            patterns,
            crawlMeta is null
                ? null
                : new NicheProfileSiteCrawlMetaRow(
                    crawlMeta.NicheProfileId,
                    crawlMeta.PagesAttempted,
                    crawlMeta.PagesFetched)));
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
        pageSize = Math.Clamp(pageSize, 1, 5000);

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
        var candidateIds = rows.Select(x => x.Id).ToList();
        var evidenceRows = new List<(Guid TopicCandidateId, TopicEvidence Evidence)>();
        if (candidateIds.Count > 0)
        {
            var rowsWithEvidence = await db.NicheTopicCandidateEvidenceRows.AsNoTracking()
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
        return Result<NicheTopicCandidateListResult>.Success(
            new NicheTopicCandidateListResult(items, total, page, pageSize));
    }

    private static NicheTopicCandidatePage MapCandidatePage(
        NicheTopicCandidate row,
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
        var list = competitors.ToList();
        if (list.Count == 0) return Result.Success();

        var profileId = list[0].NicheProfileId;
        await db.NicheCompetitors
            .Where(c => c.NicheProfileId == profileId)
            .ExecuteDeleteAsync(ct);

        foreach (var c in list)
        {
            if (c.Id == Guid.Empty) c.Id = Guid.NewGuid();
        }
        db.NicheCompetitors.AddRange(list);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<NicheCompetitor>>> GetCompetitorsAsync(
        Guid profileId, CancellationToken ct = default)
    {
        var list = await db.NicheCompetitors.AsNoTracking()
            .Where(c => c.NicheProfileId == profileId)
            .OrderByDescending(c => c.SerpPresence)
            .ThenBy(c => c.Domain)
            .ToListAsync(ct);

        var deduped = list
            .GroupBy(c => c.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return Result<IReadOnlyList<NicheCompetitor>>.Success(deduped);
    }

    public async Task<Result> UpdateCompetitorInsightsAsync(
        NicheCompetitor competitor,
        CancellationToken ct = default)
    {
        var existing = await db.NicheCompetitors.FirstOrDefaultAsync(x => x.Id == competitor.Id, ct);
        if (existing is null)
            return Result.Failure("Competitor not found");

        db.Entry(existing).CurrentValues.SetValues(competitor);
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
            var hasRunningStep = await db.NicheProfileStepRuns
                .AnyAsync(
                    x => x.NicheProfileId == profile.Id && x.Status == "running",
                    ct);

            if (hasRunningStep)
                continue;

            // Manual step re-runs can finish successfully but leave the profile in processing;
            // heal instead of marking failed when the current step row is already complete.
            if (!string.IsNullOrWhiteSpace(profile.AnalysisStep))
            {
                var currentStepComplete = await db.NicheProfileStepRuns
                    .AnyAsync(
                        x => x.NicheProfileId == profile.Id
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
        NicheAnalysisStepLogEntry? entry = null,
        CancellationToken ct = default)
    {
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var row = await db.NicheProfileStepRuns
            .FirstOrDefaultAsync(
                x => x.NicheProfileId == profileId && x.StepSlug == slug,
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
            profile.AnalysisStepLog = NicheAnalysisStepLogJson.Append(profile.AnalysisStepLog, entry);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> InvalidateDownstreamStepsAsync(
        Guid profileId,
        IReadOnlyList<string> downstreamSlugs,
        CancellationToken ct = default)
    {
        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result.Failure("Niche profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var slugSet = downstreamSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await db.NicheProfileStepRuns
            .Where(x => x.NicheProfileId == profileId && slugSet.Contains(x.StepSlug))
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
        var profile = await db.NicheProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
            return Result.Failure("Niche profile not found");

        profile.CrawledUrlsJson = crawledUrlsJson;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetStepStatusesAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        var profileExists = await db.NicheProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
            return Result<IReadOnlyDictionary<string, string>>.Failure("Niche profile not found");

        await EnsureStepRunsAsync(profileId, ct);
        var rows = await db.NicheProfileStepRuns.AsNoTracking()
            .Where(x => x.NicheProfileId == profileId)
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
        var existingSlugs = await db.NicheProfileStepRuns
            .Where(x => x.NicheProfileId == profileId)
            .Select(x => x.StepSlug)
            .ToListAsync(ct);
        var existing = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = NicheStepRunDefaults.Ordered
            .Where(step => !existing.Contains(step.StepSlug))
            .ToList();
        if (missing.Count == 0)
            return;

        foreach (var (stepNumber, stepSlug) in missing)
        {
            db.NicheProfileStepRuns.Add(new NicheProfileStepRun
            {
                NicheProfileId = profileId,
                StepNumber = stepNumber,
                StepSlug = stepSlug,
                Status = "pending",
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
