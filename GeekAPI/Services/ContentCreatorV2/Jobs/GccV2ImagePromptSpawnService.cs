using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

public sealed record ImagePromptSectionMeta(
    Guid SourceJobId,
    string SourceType,
    string Heading,
    int Order);

public sealed record ImagePromptSpawnTarget(string SourceType, string Heading, int Order);

public sealed record SpawnResult(
    int Spawned,
    int SkippedExisting,
    string? FailureReason,
    string? SkippedReason)
{
    public bool NotApplicable =>
        Spawned == 0
        && SkippedExisting == 0
        && FailureReason is null
        && SkippedReason is null;
}

/// <summary>
/// After a generate job reaches <c>ready</c>, spawns one <c>image-prompt</c> job per §3.1 target.
/// Idempotent on <c>(sourceJobId, sourceType, order)</c>.
/// </summary>
public sealed class GccV2ImagePromptSpawnService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions ContentDocJson = CreateContentDocJson();

    private static JsonSerializerOptions CreateContentDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private static readonly HashSet<string> SpawnSourceTypes =
    [
        "pillar", "blog", "tool", "email", "social", "ads",
    ];

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2JobWake _wake;
    private readonly ILogger<GccV2ImagePromptSpawnService> _logger;

    public GccV2ImagePromptSpawnService(
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        ILogger<GccV2ImagePromptSpawnService> logger)
    {
        _repo = repo;
        _wake = wake;
        _logger = logger;
    }

    public async Task<SpawnResult> SpawnForReadyJobAsync(GccV2JobDto sourceJob, CancellationToken ct)
    {
        var contentType = (sourceJob.ContentType ?? "").Trim().ToLowerInvariant();
        if (!SpawnSourceTypes.Contains(contentType))
            return new SpawnResult(0, 0, null, null);

        if (string.IsNullOrWhiteSpace(sourceJob.ResultJson))
            return new SpawnResult(0, 0, "Source job has no ResultJson.", null);

        SourceResultPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SourceResultPayload>(sourceJob.ResultJson, ContentDocJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse ResultJson for spawn on job {JobId}.", sourceJob.Id);
            return new SpawnResult(0, 0, $"Could not parse source ResultJson: {ex.Message}", null);
        }

        if (payload?.Document is null)
            return new SpawnResult(0, 0, "Source job ResultJson has no document.", null);

        var title = string.IsNullOrWhiteSpace(payload.Title) ? contentType : payload.Title.Trim();
        var targets = BuildTargets(contentType, title, payload.Document);
        if (targets.Count == 0)
        {
            return new SpawnResult(
                0,
                0,
                null,
                $"No image-prompt targets for content type '{contentType}'.");
        }

        var sourceBrief = await _repo.GetBriefAsync(sourceJob.BriefId, ct);
        var targetKeyword = sourceBrief?.TargetKeyword ?? title;

        var existing = await LoadExistingSpawnKeysAsync(sourceJob.CreateId, ct);
        var spawned = 0;
        var skippedExisting = 0;

        try
        {
            foreach (var target in targets)
            {
                var key = (sourceJob.Id, target.SourceType, target.Order);
                if (existing.Contains(key))
                {
                    skippedExisting++;
                    continue;
                }

                var briefJson = JsonSerializer.Serialize(new
                {
                    imagePromptSection = new
                    {
                        sourceJobId = sourceJob.Id,
                        sourceType = target.SourceType,
                        heading = target.Heading,
                        order = target.Order,
                    },
                }, JsonOpts);

                var brief = await _repo.CreateBriefAsync(
                    new CreateGccV2BriefCommand(
                        sourceJob.CreateId,
                        targetKeyword,
                        "image-prompt",
                        RawBriefJson: briefJson),
                    ct);

                var child = await _repo.CreateJobAsync(
                    new CreateGccV2JobCommand(
                        sourceJob.CreateId,
                        sourceJob.OwnerUserId,
                        "image-prompt",
                        brief.Id,
                        sourceJob.SiteAnalysisProfileId,
                        InitialStage: "write"),
                    ct);

                _wake.Wake(child.Id);
                existing.Add(key);
                spawned++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image-prompt spawn failed partway for source job {JobId}.", sourceJob.Id);
            return new SpawnResult(spawned, skippedExisting, ex.Message, null);
        }

        if (spawned > 0)
        {
            _logger.LogInformation(
                "Spawned {Count} image-prompt job(s) from {ContentType} job {JobId} on create {CreateId}.",
                spawned,
                contentType,
                sourceJob.Id,
                sourceJob.CreateId);
        }

        return new SpawnResult(spawned, skippedExisting, null, null);
    }

    /// <summary>§3.1 spawn table — pillar/blog heroes + H2 sections (FAQ excluded); one companion per short-form type.</summary>
    public static IReadOnlyList<ImagePromptSpawnTarget> BuildTargets(
        string contentType,
        string title,
        ContentDocument? document)
    {
        var normalized = (contentType ?? "").Trim().ToLowerInvariant();
        var targets = new List<ImagePromptSpawnTarget>();

        switch (normalized)
        {
            case "pillar":
                targets.Add(new ImagePromptSpawnTarget("pillar-hero", title, 0));
                var pillarOrder = 1;
                foreach (var section in document?.Sections ?? [])
                {
                    if (PillarSectionClassifier.IsFaqSectionTitle(section.Heading)) continue;
                    targets.Add(new ImagePromptSpawnTarget("pillar", section.Heading, pillarOrder++));
                }
                break;

            case "blog":
                targets.Add(new ImagePromptSpawnTarget("blog-hero", title, 0));
                var blogOrder = 1;
                foreach (var section in document?.Sections ?? [])
                {
                    if (PillarSectionClassifier.IsFaqSectionTitle(section.Heading)) continue;
                    targets.Add(new ImagePromptSpawnTarget("blog", section.Heading, blogOrder++));
                }
                break;

            case "tool":
                targets.Add(new ImagePromptSpawnTarget("tool", title, 1));
                break;

            case "email":
                targets.Add(new ImagePromptSpawnTarget("email", title, 0));
                break;

            case "social":
                targets.Add(new ImagePromptSpawnTarget("social", title, 0));
                break;

            case "ads":
                targets.Add(new ImagePromptSpawnTarget("ads", title, 0));
                break;
        }

        return targets;
    }

    private async Task<HashSet<(Guid SourceJobId, string SourceType, int Order)>> LoadExistingSpawnKeysAsync(
        Guid createId,
        CancellationToken ct)
    {
        var keys = new HashSet<(Guid, string, int)>();
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        foreach (var job in jobs.Where(j => string.Equals(j.ContentType, "image-prompt", StringComparison.OrdinalIgnoreCase)))
        {
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            if (brief is null || string.IsNullOrWhiteSpace(brief.RawBriefJson)) continue;
            var meta = ParseImagePromptSection(brief.RawBriefJson);
            if (meta is null) continue;
            keys.Add((meta.SourceJobId, meta.SourceType, meta.Order));
        }

        return keys;
    }

    public static ImagePromptSectionMeta? ParseImagePromptSection(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("imagePromptSection", out var el)) return null;
            var sourceJobId = el.TryGetProperty("sourceJobId", out var sj) && sj.TryGetGuid(out var id) ? id : Guid.Empty;
            var sourceType = el.TryGetProperty("sourceType", out var st) ? st.GetString() ?? "" : "";
            var heading = el.TryGetProperty("heading", out var h) ? h.GetString() ?? "" : "";
            var order = el.TryGetProperty("order", out var o) && o.TryGetInt32(out var ord) ? ord : 0;
            if (sourceJobId == Guid.Empty || string.IsNullOrWhiteSpace(sourceType)) return null;
            return new ImagePromptSectionMeta(sourceJobId, sourceType, heading, order);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SourceResultPayload(string? Title, ContentDocument? Document);
}
