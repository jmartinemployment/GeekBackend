using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerWorker : BackgroundService
{
    private readonly GeekCrawlerWake _wake;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GeekCrawlerRunCoordinator _coordinator;
    private readonly GeekCrawlerOptions _options;
    private readonly ILogger<GeekCrawlerWorker> _logger;
    private readonly int _workerIndex;

    public GeekCrawlerWorker(
        GeekCrawlerWake wake,
        IServiceScopeFactory scopeFactory,
        GeekCrawlerRunCoordinator coordinator,
        GeekCrawlerOptions options,
        ILogger<GeekCrawlerWorker> logger,
        int workerIndex = 0)
    {
        _wake = wake;
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _options = options;
        _logger = logger;
        _workerIndex = workerIndex;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GeekCrawlerWorker #{WorkerIndex} starting.", _workerIndex);
        if (_workerIndex == 0)
            await WakeOrphanedRunsOnceAsync(stoppingToken).ConfigureAwait(false);

        await foreach (var runId in _wake.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_coordinator.TryRegister(runId, out var runCt))
            {
                _logger.LogInformation(
                    "GeekCrawlerWorker #{WorkerIndex} ignoring duplicate wake for in-flight run {RunId}.",
                    _workerIndex,
                    runId);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GeekCrawlerService>();
                await service.ExecuteRunAsync(runId, runCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "GeekCrawlerWorker #{WorkerIndex} run {RunId} cancelled.",
                    _workerIndex,
                    runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeekCrawlerWorker #{WorkerIndex} failed for run {RunId}", _workerIndex, runId);
            }
            finally
            {
                _coordinator.Unregister(runId);
            }
        }
    }

    private async Task WakeOrphanedRunsOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGeekCrawlerRepository>();
            var now = DateTimeOffset.UtcNow;

            var pending = await repo.GetRunsByStatusAsync("pending", limit: 200, ct).ConfigureAwait(false);
            var pendingCandidates = pending.Where(r => GeekCrawlerRecovery.ShouldWakeAtStartup(r, now)).ToList();
            var pendingToWake = new List<GeekCrawlerRunDto>();
            foreach (var run in pendingCandidates)
            {
                // Local Mac / seeds-only must not spend the single worker resume-loading soft
                // sites that already have thousands of pages — that starves hard seeds.
                if (_options.SeedsOnly || _options.WakeZeroPagePendingOnly)
                {
                    var activity = await repo.GetPageActivityAsync(run.Id, ct).ConfigureAwait(false);
                    if (activity is { PageCount: > 0 })
                        continue;
                }

                pendingToWake.Add(run);
            }

            foreach (var run in pendingToWake)
                _wake.Wake(run.Id);

            if (pendingToWake.Count > 0)
            {
                _logger.LogInformation(
                    "Startup pending recovery woke {Count} orphaned Geek-Crawler run(s).",
                    pendingToWake.Count);
            }

            // Local / seeds-only should not steal in-flight Railway crawls via running-orphan
            // recovery (that double-wakes the pending run and cancels the first worker mid-fetch).
            if (_options.SeedsOnly || _options.WakeZeroPagePendingOnly)
                return;

            var running = await repo.GetRunsByStatusAsync("running", limit: 200, ct).ConfigureAwait(false);
            var runningRecovered = 0;
            foreach (var run in running)
            {
                var activity = await repo.GetPageActivityAsync(run.Id, ct).ConfigureAwait(false);
                if (activity is null)
                    continue;

                var shouldRecover = activity.PageCount == 0
                    ? GeekCrawlerRecovery.ShouldRecoverRunningOrphan(run, now, hasSavedPages: false)
                    : activity.LastCrawledAtUtc is DateTimeOffset last
                        && GeekCrawlerRecovery.ShouldRecoverStalledRunning(run, now, last);

                if (!shouldRecover)
                    continue;

                await repo.PatchRunAsync(
                    run.Id,
                    new PatchGeekCrawlerRunCommand(Status: "pending"),
                    ct).ConfigureAwait(false);
                _wake.Wake(run.Id);
                runningRecovered++;
            }

            if (runningRecovered > 0)
            {
                _logger.LogInformation(
                    "Startup running recovery reset and woke {Count} stalled Geek-Crawler run(s).",
                    runningRecovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup Geek-Crawler orphan scan failed; continuing without recovery.");
        }
    }
}
