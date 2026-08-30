using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerWorker : BackgroundService
{
    private readonly GeekCrawlerWake _wake;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeekCrawlerWorker> _logger;

    public GeekCrawlerWorker(
        GeekCrawlerWake wake,
        IServiceScopeFactory scopeFactory,
        ILogger<GeekCrawlerWorker> logger)
    {
        _wake = wake;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GeekCrawlerWorker starting.");
        await WakeOrphanedPendingOnceAsync(stoppingToken).ConfigureAwait(false);

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
                _logger.LogError(ex, "GeekCrawlerWorker failed for run {RunId}", runId);
            }
        }
    }

    private async Task WakeOrphanedPendingOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGeekCrawlerRepository>();
            var pending = await repo.GetRunsByStatusAsync("pending", limit: 200, ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var toWake = pending.Where(r => GeekCrawlerRecovery.ShouldWakeAtStartup(r, now)).ToList();
            foreach (var run in toWake)
                _wake.Wake(run.Id);

            if (toWake.Count > 0)
            {
                _logger.LogInformation(
                    "Startup pending recovery woke {Count} orphaned Geek-Crawler run(s).",
                    toWake.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup pending Geek-Crawler scan failed; continuing without recovery.");
        }
    }
}
