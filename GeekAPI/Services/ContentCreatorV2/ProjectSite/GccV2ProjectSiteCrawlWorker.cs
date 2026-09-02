using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public sealed class GccV2ProjectSiteCrawlWorker : BackgroundService
{
    private readonly GccV2ProjectSiteCrawlWake _wake;
    private readonly GccV2ProjectSiteCrawlRunCoordinator _coordinator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GccV2ProjectSiteCrawlWorker> _logger;

    public GccV2ProjectSiteCrawlWorker(
        GccV2ProjectSiteCrawlWake wake,
        GccV2ProjectSiteCrawlRunCoordinator coordinator,
        IServiceScopeFactory scopeFactory,
        ILogger<GccV2ProjectSiteCrawlWorker> logger)
    {
        _wake = wake;
        _coordinator = coordinator;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GccV2ProjectSiteCrawlWorker starting.");
        await WakeOrphanedRunsOnceAsync(stoppingToken).ConfigureAwait(false);

        await foreach (var runId in _wake.Reader.ReadAllAsync(stoppingToken))
        {
            var runCt = _coordinator.Register(runId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GccV2ProjectSiteCrawlService>();
                await service.ExecuteRunAsync(runId, runCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("GccV2ProjectSiteCrawlWorker run {RunId} cancelled.", runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GccV2ProjectSiteCrawlWorker failed for run {RunId}", runId);
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
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
            var pending = await repo.GetProjectSiteCrawlRunsByStatusAsync("pending", 200, ct).ConfigureAwait(false);
            foreach (var run in pending)
                _wake.Wake(run.Id);

            if (pending.Count > 0)
            {
                _logger.LogInformation(
                    "Startup recovery woke {Count} pending project-site crawl run(s).",
                    pending.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup project-site crawl orphan scan failed.");
        }
    }
}
