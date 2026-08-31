using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>
/// Spawns partner tool jobs idempotently on <c>(createId, partnerSlug)</c>.
/// Triggered when pillar reaches <c>ready</c> (pillar + tool also-draft), or when the overview
/// tool job starts WRITE on a tool-primary create (no pillar sibling).
/// </summary>
public sealed class GccV2ToolPageSpawnService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2JobWake _wake;
    private readonly GccV2ToolResearchExtractor _extractor;
    private readonly IContentProviderFactory _providers;
    private readonly ILogger<GccV2ToolPageSpawnService> _logger;

    public GccV2ToolPageSpawnService(
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        GccV2ToolResearchExtractor extractor,
        IContentProviderFactory providers,
        ILogger<GccV2ToolPageSpawnService> logger)
    {
        _repo = repo;
        _wake = wake;
        _extractor = extractor;
        _providers = providers;
        _logger = logger;
    }

    public async Task<SpawnResult> SpawnForReadyPillarAsync(GccV2JobDto pillarJob, CancellationToken ct)
    {
        if (!string.Equals(pillarJob.ContentType, "pillar", StringComparison.OrdinalIgnoreCase))
            return new SpawnResult(0, 0, null, null);

        var result = await EnsurePartnersSpawnedAsync(pillarJob, ct);
        if (result.FailureReason is not null || result.NotApplicable)
            return result;

        await WakeOverviewJobAsync(pillarJob.CreateId, ct);
        return result;
    }

    /// <summary>
    /// Idempotent partner spawn from the trigger job's brief. Used for tool-primary creates at overview WRITE
    /// and shared by <see cref="SpawnForReadyPillarAsync"/>.
    /// </summary>
    public async Task<SpawnResult> EnsurePartnersSpawnedAsync(GccV2JobDto triggerJob, CancellationToken ct)
    {
        var brief = await _repo.GetBriefAsync(triggerJob.BriefId, ct);
        if (brief is null || string.IsNullOrWhiteSpace(brief.RawBriefJson))
            return new SpawnResult(0, 0, "Brief missing for partner tool spawn.", null);

        if (!BriefIncludesToolDraft(brief.RawBriefJson))
            return new SpawnResult(0, 0, null, null);

        var partnerResearch = ParsePartnerResearch(brief.RawBriefJson);
        var partnerRows = GccV2PartnerUrlResearchService.CollectPartnerToolRows(brief.RawBriefJson);
        if (partnerRows.Count == 0)
        {
            _logger.LogWarning(
                "Tool draft checked on create {CreateId} but no partner tools resolved — overview only.",
                triggerJob.CreateId);
        }

        var provider = _providers.GetDefault();
        var existing = await LoadExistingPartnerSlugsAsync(triggerJob.CreateId, ct);
        var spawned = 0;
        var skippedExisting = 0;
        var order = 1;

        try
        {
            foreach (var row in partnerRows)
            {
                var slug = GccV2ToolSlugHelper.SlugifyToolName(row.Name);
                if (existing.Contains(slug))
                {
                    skippedExisting++;
                    order++;
                    continue;
                }

                var extracted = await _extractor.ExtractAsync(provider, row.Name, row.Url, partnerResearch, ct);
                var briefJson = GccV2ToolPageTargetParser.SerializePartnerBriefSlice(
                    row.Name,
                    slug,
                    row.Url,
                    extracted,
                    order);
                var pagesForTool = FilterPartnerResearchForUrl(partnerResearch, row.Url);
                if (pagesForTool.Count > 0)
                {
                    briefJson = GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(
                        briefJson,
                        pagesForTool)!;
                }

                var childBrief = await _repo.CreateBriefAsync(
                    new CreateGccV2BriefCommand(
                        triggerJob.CreateId,
                        brief.TargetKeyword,
                        "tool",
                        RawBriefJson: briefJson),
                    ct);

                var child = await _repo.CreateJobAsync(
                    new CreateGccV2JobCommand(
                        triggerJob.CreateId,
                        triggerJob.OwnerUserId,
                        "tool",
                        childBrief.Id,
                        triggerJob.SiteAnalysisProfileId,
                        InitialStage: "write"),
                    ct);

                _wake.Wake(child.Id);
                existing.Add(slug);
                spawned++;
                order++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool page spawn failed partway for job {JobId}.", triggerJob.Id);
            return new SpawnResult(spawned, skippedExisting, ex.Message, null);
        }

        var rewakenedFailed = await RewakeFailedPartnerJobsAsync(triggerJob.CreateId, ct);
        if (rewakenedFailed > 0)
        {
            _logger.LogInformation(
                "Re-wake {Count} failed partner tool job(s) on create {CreateId}.",
                rewakenedFailed,
                triggerJob.CreateId);
        }

        if (spawned > 0)
        {
            _logger.LogInformation(
                "Spawned {Count} partner tool job(s) from job {JobId} on create {CreateId}.",
                spawned,
                triggerJob.Id,
                triggerJob.CreateId);
        }

        var skippedReason = partnerRows.Count == 0 ? "No partner tools resolved — overview only." : null;
        return new SpawnResult(spawned, skippedExisting, null, skippedReason);
    }

    public static bool BriefIncludesToolDraft(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("contentTypes", out var types)
                || types.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var t in types.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String
                    && string.Equals(t.GetString(), "tool", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private async Task WakeOverviewJobAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        foreach (var job in jobs.Where(j => string.Equals(j.ContentType, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(job.Stage, "write", StringComparison.OrdinalIgnoreCase)) continue;
            if (job.Status is "ready" or "failed" or "canceled") continue;

            var jobBrief = await _repo.GetBriefAsync(job.BriefId, ct);
            var target = GccV2ToolPageTargetParser.Parse(jobBrief?.RawBriefJson);
            if (target is null || !target.IsOverview) continue;

            await _repo.PatchJobAsync(job.Id, new PatchGccV2JobCommand(
                Stage: "write",
                Status: "pending",
                ReleaseClaim: true,
                Wake: true), ct);
        }
    }

    internal static bool ShouldRewakeFailedPartner(GccV2JobDto job, GccV2ToolPageTarget? target) =>
        string.Equals(job.ContentType, "tool", StringComparison.OrdinalIgnoreCase)
        && string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase)
        && target is not null
        && target.IsPartner;

    private async Task<int> RewakeFailedPartnerJobsAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var count = 0;
        foreach (var job in jobs)
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var target = GccV2ToolPageTargetParser.Parse(brief?.RawBriefJson);
            if (!ShouldRewakeFailedPartner(job, target)) continue;

            await _repo.PatchJobAsync(job.Id, new PatchGccV2JobCommand(
                Status: "pending",
                ReleaseClaim: true,
                Wake: true), ct);
            _wake.Wake(job.Id);
            count++;
        }

        return count;
    }

    private async Task<HashSet<string>> LoadExistingPartnerSlugsAsync(Guid createId, CancellationToken ct)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        foreach (var job in jobs.Where(j => string.Equals(j.ContentType, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var target = GccV2ToolPageTargetParser.Parse(brief?.RawBriefJson);
            if (target is null || !target.IsPartner || string.IsNullOrWhiteSpace(target.Slug)) continue;
            slugs.Add(target.Slug);
        }

        return slugs;
    }

    private static IReadOnlyList<GccQuoteablePage> ParsePartnerResearch(string rawBriefJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("partnerResearch", out var el)
                || el.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<GccQuoteablePage>>(el.GetRawText(), JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<GccQuoteablePage> FilterPartnerResearchForUrl(
        IReadOnlyList<GccQuoteablePage> pages,
        string? sourceUrl)
    {
        if (pages.Count == 0 || string.IsNullOrWhiteSpace(sourceUrl)) return pages;
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)) return pages;

        var host = sourceUri.Host;
        var matched = pages.Where(p =>
        {
            if (string.IsNullOrWhiteSpace(p.Url)) return false;
            if (string.Equals(p.Url, sourceUrl, StringComparison.OrdinalIgnoreCase)) return true;
            return Uri.TryCreate(p.Url, UriKind.Absolute, out var u)
                   && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        return matched.Count > 0 ? matched : pages;
    }
}
