using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Geo;

/// <summary>Published CMS location at snapshot time — mirrors the fields a Canvas "AI visibility"
/// panel needs from a <c>GccV2PublishRecord</c> without re-fetching the whole audit trail.</summary>
public sealed record AiVisibilityPublishedUrl(
    string Channel,
    string? Slug,
    string? PublicUrl,
    string Status,
    bool IsPublished,
    DateTimeOffset? PublishedAtUtc);

/// <summary>The full "AI-visibility readiness" report — serialized verbatim into
/// <c>GccV2AiVisibilitySnapshot.ReportJson</c>. Dual SEO/GEO scores travel together (Frase-style),
/// plus the OverlapGate/ship-ready summary from the create's last VALIDATE pass and any CMS URLs
/// already published for it.</summary>
public sealed record AiVisibilityReport(
    string TargetKeyword,
    int SeoScore,
    int GeoScore,
    IReadOnlyList<GccV2GeoAnalyzer.GeoCheck> GeoChecks,
    string GeoSummary,
    int OverlapHitCount,
    bool ShipReady,
    bool OutstandingIssues,
    IReadOnlyList<AiVisibilityPublishedUrl> PublishedUrls,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Backlog "AI-visibility" slice: not a live ChatGPT/Perplexity citation tracker (no external
/// calls) — builds a readiness snapshot from the create's latest completed job by re-running the
/// same dual SEO (<c>GcwSeoAnalyzer</c>) + GEO (<see cref="GccV2GeoAnalyzer"/>) analyzers VALIDATE
/// already uses, preferring the persisted VALIDATE stage result's scores/overlap summary when one
/// exists, and attaching any CMS URLs already published via <c>GccV2CmsPublishService</c>.
/// </summary>
public sealed class GccV2AiVisibilityService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ContentDocJson = CreateContentDocJson();

    private static JsonSerializerOptions CreateContentDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2AiVisibilityService> _logger;

    public GccV2AiVisibilityService(HttpGccV2Repository repo, ILogger<GccV2AiVisibilityService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public Task<GccV2AiVisibilitySnapshotDto?> GetLatestAsync(Guid createId, CancellationToken ct) =>
        _repo.GetLatestAiVisibilitySnapshotAsync(createId, ct);

    public Task<IReadOnlyList<GccV2AiVisibilitySnapshotDto>> ListAsync(Guid createId, CancellationToken ct) =>
        _repo.ListAiVisibilitySnapshotsByCreateAsync(createId, ct);

    /// <summary>Rebuilds a snapshot from the create's latest job and persists it. Throws
    /// <see cref="InvalidOperationException"/> (caller maps to a 4xx) when there is no completed
    /// draft yet to score.</summary>
    public async Task<GccV2AiVisibilitySnapshotDto> BuildAndPersistAsync(GccV2CreateDto create, CancellationToken ct)
    {
        var job = await _repo.GetLatestJobByCreateAsync(create.Id, ct);
        if (job is null || string.IsNullOrWhiteSpace(job.ResultJson))
            throw new InvalidOperationException("No completed draft yet for this create — generate content first.");

        JobResultPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JobResultPayload>(job.ResultJson, ContentDocJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not parse ResultJson for job {JobId} during AI-visibility scoring.", job.Id);
            throw new InvalidOperationException("Job result could not be parsed.");
        }

        if (payload is not { Document: { } document })
            throw new InvalidOperationException("Job has no completed document to analyze.");

        var targetKeyword = await LoadTargetKeywordAsync(job.BriefId, ct);
        var analyzerJson = GccV2AnalyzerDocument.Serialize(document);
        var geo = GccV2GeoAnalyzer.Analyze(analyzerJson, targetKeyword);
        var validation = await LoadValidationSummaryAsync(job.Id, ct);
        var seoScore = validation.SeoScore ?? GcwSeoAnalyzer.Analyze(analyzerJson, targetKeyword).Score;

        var publishRecords = await _repo.ListPublishRecordsByCreateAsync(create.Id, ct);
        var publishedUrls = publishRecords
            .Where(r => !string.IsNullOrWhiteSpace(r.PublicUrl))
            .Select(r => new AiVisibilityPublishedUrl(r.Channel, r.Slug, r.PublicUrl, r.Status, r.IsPublished, r.PublishedAtUtc))
            .ToList();

        var report = new AiVisibilityReport(
            targetKeyword,
            seoScore,
            geo.Score,
            geo.Checks,
            geo.Summary,
            validation.OverlapHitCount,
            validation.ShipReady,
            validation.OutstandingIssues,
            publishedUrls,
            DateTimeOffset.UtcNow);

        var overallScore = (int)Math.Round((seoScore + geo.Score) / 2.0);

        return await _repo.CreateAiVisibilitySnapshotAsync(
            new CreateGccV2AiVisibilitySnapshotCommand(
                create.Id,
                job.Id,
                create.OwnerUserId,
                overallScore,
                JsonSerializer.Serialize(report, JsonOpts)),
            ct);
    }

    private async Task<string> LoadTargetKeywordAsync(Guid briefId, CancellationToken ct)
    {
        if (briefId == Guid.Empty) return "";
        try
        {
            var brief = await _repo.GetBriefAsync(briefId, ct);
            return brief?.TargetKeyword ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load brief {BriefId} for AI-visibility scoring.", briefId);
            return "";
        }
    }

    /// <summary>Prefers the create's last persisted VALIDATE stage result (post guardrail-clean,
    /// post-repair) for SEO score/overlap/ship-ready — falling back to "unknown" (fresh SEO is
    /// still computed by the caller) if VALIDATE never ran for this job, e.g. social/image-prompt
    /// content types that skip it.</summary>
    private async Task<ValidationSummary> LoadValidationSummaryAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            var results = await _repo.GetStageResultsAsync(jobId, ct);
            var latest = results
                .Where(r => string.Equals(r.Stage, "validate", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CompletedAtUtc)
                .FirstOrDefault();
            if (latest is null) return ValidationSummary.Empty;

            using var doc = JsonDocument.Parse(latest.OutputJson);
            var root = doc.RootElement;
            int? seoScore = root.TryGetProperty("seoScore", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32() : null;
            var overlapCount = root.TryGetProperty("overlapHits", out var oh) && oh.ValueKind == JsonValueKind.Array
                ? oh.GetArrayLength() : 0;
            var shipReady = root.TryGetProperty("shipReady", out var sr) && sr.ValueKind == JsonValueKind.True;
            var outstanding = root.TryGetProperty("outstandingIssues", out var oi) && oi.ValueKind == JsonValueKind.True;
            return new ValidationSummary(seoScore, overlapCount, shipReady, outstanding);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse VALIDATE stage result for job {JobId}; scoring SEO fresh.", jobId);
            return ValidationSummary.Empty;
        }
    }

    private sealed record ValidationSummary(int? SeoScore, int OverlapHitCount, bool ShipReady, bool OutstandingIssues)
    {
        public static readonly ValidationSummary Empty = new(null, 0, false, false);
    }

    private sealed record JobResultPayload(
        string? Title,
        string? MetaDescription,
        ContentDocument? Document,
        bool? ShipReady,
        bool? OutstandingIssues);
}
