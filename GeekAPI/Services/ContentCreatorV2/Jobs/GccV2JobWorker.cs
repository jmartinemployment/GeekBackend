using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>
/// Drives dummy multi-stage jobs end-to-end: PLAN (pauses for outline approval) then
/// WRITE → VALIDATE → done. Wakes only from <see cref="GccV2JobWake"/> — the only "loop" here is
/// draining that Channel, plus exactly one expired-lease scan at startup. No pending-job polling.
/// </summary>
public sealed class GccV2JobWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions ContentDocJson = CreateContentDocJson();

    private static JsonSerializerOptions CreateContentDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly GccV2JobWake _wake;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GccV2JobWorker> _logger;

    public GccV2JobWorker(GccV2JobWake wake, IServiceScopeFactory scopeFactory, ILogger<GccV2JobWorker> logger)
    {
        _wake = wake;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GccV2JobWorker starting (instance {InstanceId}).", _instanceId);

        await ReclaimExpiredLeasesOnceAsync(stoppingToken);

        try
        {
            while (await _wake.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_wake.Reader.TryRead(out var jobId))
                {
                    try
                    {
                        await ProcessJobAsync(jobId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GccV2JobWorker failed processing job {JobId}", jobId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Exactly one scan, at startup, for jobs left <c>running</c> with an expired lease (a worker
    /// crashed mid-job). Wakes them so this pass can reclaim — never repeats, never sleeps/ticks.
    /// </summary>
    private async Task ReclaimExpiredLeasesOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
            var stuck = await repo.GetJobsByStatusAsync("running", DateTimeOffset.UtcNow, limit: 200, ct);
            foreach (var job in stuck)
            {
                _logger.LogWarning("Reclaiming job {JobId} — lease expired while running.", job.Id);
                _wake.Wake(job.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup expired-lease scan failed; continuing without reclaim.");
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var writer = scope.ServiceProvider.GetRequiredService<GccV2JobEventWriter>();

        var claimed = await repo.ClaimJobAsync(jobId, _instanceId, leaseSeconds: 120, ct);
        if (claimed is null)
        {
            _logger.LogDebug("Job {JobId} not claimable right now (already claimed or terminal).", jobId);
            return;
        }

        var ownerUserId = ParseOwner(claimed.OwnerUserId);

        if (string.Equals(claimed.Stage, "plan", StringComparison.OrdinalIgnoreCase))
        {
            var planService = scope.ServiceProvider.GetRequiredService<GccV2PlanService>();
            await RunPlanStageAsync(jobId, ownerUserId, claimed, repo, writer, planService, ct);
            return;
        }

        var writeService = scope.ServiceProvider.GetRequiredService<GccV2WriteService>();
        var validateService = scope.ServiceProvider.GetRequiredService<GccV2ValidateService>();
        await RunWriteThenValidateStageAsync(jobId, ownerUserId, claimed, repo, writer, writeService, validateService, ct);
    }

    /// <summary>
    /// Real PLAN: loads the brief and builds the outline via <see cref="GccV2PlanService"/> —
    /// grounded in the site's real page-section hierarchy when Generate prefetched a match onto
    /// the brief, otherwise content-type-aware templates — then pauses for approval. Each section
    /// carries a distinct <c>job</c> ("problem" | "advance") and its own
    /// <c>hierarchyChildHeadings</c> subset so VALIDATE's OverlapGate has real structure to diff
    /// against instead of inventing it later.
    /// </summary>
    private async Task RunPlanStageAsync(
        Guid jobId,
        Guid ownerUserId,
        GccV2JobDto job,
        HttpGccV2Repository repo,
        GccV2JobEventWriter writer,
        GccV2PlanService planService,
        CancellationToken ct)
    {
        await writer.AppendAsync(jobId, ownerUserId, "JobStageChanged", new { stage = "plan" }, ct: ct);

        var brief = await repo.GetBriefAsync(job.BriefId, ct);
        if (brief is null)
        {
            await FailJobAsync(repo, writer, jobId, ownerUserId, $"Brief {job.BriefId} not found for job {jobId}.", ct);
            return;
        }

        GccV2PlanOutline outline;
        try
        {
            outline = await planService.BuildOutlineAsync(job, brief, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PLAN failed for job {JobId}; marking failed.", jobId);
            await FailJobAsync(repo, writer, jobId, ownerUserId, $"PLAN failed: {ex.Message}", ct);
            return;
        }

        await repo.AddStageResultAsync(
            jobId,
            new CreateGccV2StageResultCommand("plan", null, JsonSerializer.Serialize(outline, JsonOpts), 0),
            ct);
        await writer.AppendAsync(jobId, ownerUserId, "OutlineReady", outline, ct: ct);

        await AnnounceBrandKitIfReadyAsync(jobId, ownerUserId, job, repo, writer, ct);

        await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(Status: "awaiting_outline_approval", ReleaseClaim: true), ct);
    }

    /// <summary>
    /// If Generate supplied a <see cref="GccV2JobDto.SiteAnalysisProfileId"/>, look up the brand
    /// kit derived for it (built/persisted synchronously in <c>GccV2Controller.Generate</c>) and
    /// emit its summary. Missing/unbuildable kits are silently skipped — Generate already logs the
    /// failure, and PLAN must still be able to complete without one.
    /// </summary>
    private static async Task AnnounceBrandKitIfReadyAsync(
        Guid jobId,
        Guid ownerUserId,
        GccV2JobDto job,
        HttpGccV2Repository repo,
        GccV2JobEventWriter writer,
        CancellationToken ct)
    {
        if (job.SiteAnalysisProfileId is not { } profileId) return;

        var kits = await repo.ListBrandKitsByProfileAsync(profileId, ct);
        var kit = kits.FirstOrDefault();
        if (kit is null) return;

        using var doc = JsonDocument.Parse(kit.KitJson);
        var root = doc.RootElement;
        string? companyName = root.TryGetProperty("companyName", out var cn) ? cn.GetString() : null;
        string? website = root.TryGetProperty("website", out var w) ? w.GetString() : null;
        var notesCount = root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array
            ? notes.GetArrayLength()
            : 0;

        await writer.AppendAsync(jobId, ownerUserId, "BrandKitReady", new
        {
            brandKitId = kit.Id,
            derivedFromProfileId = kit.DerivedFromProfileId,
            voiceStatus = kit.VoiceStatus,
            companyName,
            website,
            notesCount,
        }, ct: ct);
    }

    /// <summary>How long each claim lease extension buys — comfortably longer than one section's
    /// LLM round trip, refreshed after every section so a multi-section pillar never lets its lease
    /// lapse mid-job.</summary>
    private const int LeaseExtensionSeconds = 180;

    /// <summary>Real WRITE → VALIDATE (+REPAIR) → done, run after outline approval moved the job
    /// back to pending. One real LLM call per section (via <see cref="GccV2WriteService"/>); no
    /// fake delays. The claim lease is extended after every section/repair write so long pillars
    /// with several sequential LLM calls never lose their claim mid-job.</summary>
    private async Task RunWriteThenValidateStageAsync(
        Guid jobId,
        Guid ownerUserId,
        GccV2JobDto job,
        HttpGccV2Repository repo,
        GccV2JobEventWriter writer,
        GccV2WriteService writeService,
        GccV2ValidateService validateService,
        CancellationToken ct)
    {
        if (await StopIfCanceledAsync(repo, jobId, ct)) return;

        await writer.AppendAsync(jobId, ownerUserId, "JobStageChanged", new { stage = "write" }, ct: ct);

        Task ExtendLease(CancellationToken innerCt) =>
            repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(LeaseUntilUtc: DateTimeOffset.UtcNow.AddSeconds(LeaseExtensionSeconds)), innerCt);

        GccV2WriteContext wc;
        try
        {
            var prepared = await writeService.PrepareAsync(job, ct);
            wc = prepared with { ExtendLease = ExtendLease };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WRITE preparation failed for job {JobId}; marking failed.", jobId);
            await FailJobAsync(repo, writer, jobId, ownerUserId, $"WRITE preparation failed: {ex.Message}", ct);
            return;
        }

        GccV2WriteOutput written;
        try
        {
            written = await writeService.WriteAsync(wc, ownerUserId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WRITE failed for job {JobId}; marking failed.", jobId);
            await FailJobAsync(repo, writer, jobId, ownerUserId, $"WRITE failed: {ex.Message}", ct);
            return;
        }

        await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(Stage: "validate", TokensUsed: written.TokensUsed), ct);

        if (string.Equals(job.ContentType, "image-prompt", StringComparison.OrdinalIgnoreCase))
        {
            var imageDocument = written.ToContentDocument();
            var imageResultJson = JsonSerializer.Serialize(new
            {
                title = written.Title,
                metaDescription = written.MetaDescription,
                document = imageDocument,
                shipReady = true,
                outstandingIssues = false,
                writeOnly = true,
            }, ContentDocJson);

            await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(
                Stage: "done",
                Status: "ready",
                ResultJson: imageResultJson,
                ReleaseClaim: true,
                CompletedAtUtc: DateTimeOffset.UtcNow), ct);

            await writer.AppendAsync(jobId, ownerUserId, "JobCompleted", new
            {
                status = "ready",
                shipReady = true,
                outstandingIssues = false,
                writeOnly = true,
            }, ct: ct);
            return;
        }

        if (await StopIfCanceledAsync(repo, jobId, ct)) return;

        await writer.AppendAsync(jobId, ownerUserId, "JobStageChanged", new { stage = "validate" }, ct: ct);

        GccV2ValidateOutcome outcome;
        try
        {
            outcome = await validateService.RunAsync(wc, ownerUserId, written, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VALIDATE failed for job {JobId}; marking failed.", jobId);
            await FailJobAsync(repo, writer, jobId, ownerUserId, $"VALIDATE failed: {ex.Message}", ct);
            return;
        }

        var finalDocument = outcome.Final.ToContentDocument();
        var resultJson = JsonSerializer.Serialize(new
        {
            title = outcome.Final.Title,
            metaDescription = outcome.Final.MetaDescription,
            document = finalDocument,
            shipReady = outcome.ShipReady,
            outstandingIssues = outcome.OutstandingIssues,
            repairAttempts = outcome.RepairAttempts,
        }, ContentDocJson);

        await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(
            Stage: "done",
            Status: "ready",
            ResultJson: resultJson,
            ReleaseClaim: true,
            CompletedAtUtc: DateTimeOffset.UtcNow), ct);

        await writer.AppendAsync(jobId, ownerUserId, "JobCompleted", new
        {
            status = "ready",
            shipReady = outcome.ShipReady,
            outstandingIssues = outcome.OutstandingIssues,
            repairAttempts = outcome.RepairAttempts,
        }, ct: ct);
    }

    private static async Task FailJobAsync(
        HttpGccV2Repository repo, GccV2JobEventWriter writer, Guid jobId, Guid ownerUserId, string error, CancellationToken ct)
    {
        await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(
            Status: "failed",
            Error: error,
            ReleaseClaim: true,
            CompletedAtUtc: DateTimeOffset.UtcNow), ct);
        await writer.AppendAsync(jobId, ownerUserId, "JobFailed", new { error }, ct: ct);
    }

    private static async Task<bool> StopIfCanceledAsync(HttpGccV2Repository repo, Guid jobId, CancellationToken ct)
    {
        var job = await repo.GetJobAsync(jobId, ct);
        if (job is null) return true;
        if (!string.Equals(job.Status, "canceled", StringComparison.OrdinalIgnoreCase)) return false;

        await repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(ReleaseClaim: true), ct);
        return true;
    }

    private static Guid ParseOwner(string ownerUserId) => Guid.TryParse(ownerUserId, out var id) ? id : Guid.Empty;
}
