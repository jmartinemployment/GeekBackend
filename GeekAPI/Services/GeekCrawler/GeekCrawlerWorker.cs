using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerWorker : BackgroundService
{
    private readonly GeekCrawlerWake _wake;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeekCrawlerWorker> _logger;
    private readonly int _workerIndex;

    public GeekCrawlerWorker(
        GeekCrawlerWake wake,
        IServiceScopeFactory scopeFactory,
        ILogger<GeekCrawlerWorker> logger,
        int workerIndex = 0)
    {
        _wake = wake;
        _scopeFactory = scopeFactory;
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
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GeekCrawlerService>();
                await service.ExecuteRunAsync(runId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "GeekCrawlerWorker #{WorkerIndex} failed for run {RunId}", _workerIndex, runId);
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
            var pendingToWake = pending.Where(r => GeekCrawlerRecovery.ShouldWakeAtStartup(r, now)).ToList();
            foreach (var run in pendingToWake)
                _wake.Wake(run.Id);

            if (pendingToWake.Count > 0)
            {
                _logger.LogInformation(
                    "Startup pending recovery woke {Count} orphaned Geek-Crawler run(s).",
                    pendingToWake.Count);
            }

            var running = await repo.GetRunsByStatusAsync("running", limit: 200, ct).ConfigureAwait(false);
            var runningRecovered = 0;
            foreach (var run in running.Where(r => GeekCrawlerRecovery.ShouldRecoverRunningOrphan(r, now, hasSavedPages: false)))
            {
                var pages = await repo.ListPagesAsync(run.Id, limit: 1, offset: 0, ct).ConfigureAwait(false);
                if (pages.Count > 0)
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
                    "Startup running recovery reset and woke {Count} zero-page Geek-Crawler run(s).",
                    runningRecovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup Geek-Crawler orphan scan failed; continuing without recovery.");
        }
    }
}
