using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

public sealed class GccV2ToolSourceCrawlWorker : BackgroundService
{
    private readonly GccV2ToolSourceCrawlWake _wake;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GccV2ToolSourceCrawlWorker> _logger;

    public GccV2ToolSourceCrawlWorker(
        GccV2ToolSourceCrawlWake wake,
        IServiceScopeFactory scopeFactory,
        ILogger<GccV2ToolSourceCrawlWorker> logger)
    {
        _wake = wake;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GccV2ToolSourceCrawlWorker starting.");

        await WakeOrphanedPendingOnceAsync(stoppingToken);

        await foreach (var runId in _wake.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GccV2ToolSourceCrawlService>();
                await service.ExecuteRunAsync(runId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "GccV2ToolSourceCrawlWorker failed for run {RunId}", runId);
            }
        }
    }

    /// <summary>
    /// One startup scan for crawl runs left <c>pending</c> when the in-memory wake was lost
    /// (API restart, deploy, or wake on another instance). Matches <see cref="Jobs.GccV2JobWorker"/>.
    /// </summary>
    private async Task WakeOrphanedPendingOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
            var pending = await repo.GetToolSourceCrawlRunsByStatusAsync("pending", limit: 200, ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var toWake = pending.Where(r => GccV2ToolSourceCrawlRecovery.ShouldWakeAtStartup(r, now)).ToList();
            foreach (var run in toWake)
                _wake.Wake(run.Id);

            if (toWake.Count > 0)
            {
                _logger.LogInformation(
                    "Startup pending recovery woke {Count} orphaned tool-source crawl run(s).",
                    toWake.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup pending tool-source crawl scan failed; continuing without recovery.");
        }
    }
}
