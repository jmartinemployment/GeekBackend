namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerPlaywrightStartupHostedService : IHostedService
{
    private readonly GeekCrawlerPlaywrightHolder _holder;
    private readonly ILogger<GeekCrawlerPlaywrightStartupHostedService> _logger;

    public GeekCrawlerPlaywrightStartupHostedService(
        GeekCrawlerPlaywrightHolder holder,
        ILogger<GeekCrawlerPlaywrightStartupHostedService> logger)
    {
        _holder = holder;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _holder.InitializeAsync().ConfigureAwait(false);
        if (_holder.Browser is null)
            _logger.LogWarning("Geek-Crawler Playwright browser unavailable at startup.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
