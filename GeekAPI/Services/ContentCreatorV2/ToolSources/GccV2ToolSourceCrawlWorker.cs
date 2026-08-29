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
}
