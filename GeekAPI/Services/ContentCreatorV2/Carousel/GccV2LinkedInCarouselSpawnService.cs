using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.ContentCreatorV2.Jobs;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

/// <summary>
/// When a long-form job reaches <c>ready</c> and the brief includes <c>linkedin-document</c>
/// or <c>linkedin-carousel</c> in <c>contentTypes</c>, spawns one carousel job (deferred from
/// generate — carousel needs no parallel PLAN).
/// </summary>
public sealed class GccV2LinkedInCarouselSpawnService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2JobWake _wake;
    private readonly ILogger<GccV2LinkedInCarouselSpawnService> _logger;

    public GccV2LinkedInCarouselSpawnService(
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        ILogger<GccV2LinkedInCarouselSpawnService> logger)
    {
        _repo = repo;
        _wake = wake;
        _logger = logger;
    }

    public static bool BriefIncludesLinkedInCarousel(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("contentTypes", out var types) || types.ValueKind != JsonValueKind.Array)
                return false;

            return types.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String
                && GccV2ChannelTypes.IsLinkedIn(t.GetString()));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<SpawnResult> SpawnForReadyLongFormAsync(GccV2JobDto sourceJob, CancellationToken ct)
    {
        if (!GccV2LinkedInCarouselEligibility.IsEligibleSource(sourceJob.ContentType))
            return new SpawnResult(0, 0, null, null);

        var brief = await _repo.GetBriefAsync(sourceJob.BriefId, ct);
        if (brief is null || !BriefIncludesLinkedInCarousel(brief.RawBriefJson))
            return new SpawnResult(0, 0, null, null);

        var existing = await _repo.ListJobsByCreateAsync(sourceJob.CreateId, ct);
        if (existing.Any(j => GccV2ChannelTypes.IsLinkedInDocument(j.ContentType)))
            return new SpawnResult(0, 1, null, "LinkedIn carousel job already exists on this create.");

        var job = await _repo.CreateJobAsync(
            new CreateGccV2JobCommand(
                sourceJob.CreateId,
                sourceJob.OwnerUserId,
                GccV2ChannelTypes.LinkedInCarousel,
                sourceJob.BriefId,
                ProjectSiteCrawlRunId: sourceJob.ProjectSiteCrawlRunId),
            ct);

        _wake.Wake(job.Id);
        _logger.LogInformation(
            "Spawned linkedin-carousel job {JobId} from ready {ContentType} job {SourceJobId}.",
            job.Id,
            sourceJob.ContentType,
            sourceJob.Id);

        return new SpawnResult(1, 0, null, null);
    }
}
