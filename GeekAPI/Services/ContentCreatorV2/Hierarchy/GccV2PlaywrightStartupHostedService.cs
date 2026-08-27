namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>Warm Chromium once at process start so the first create/preflight is not cold-launch.</summary>
public sealed class GccV2PlaywrightStartupHostedService : IHostedService
{
    private readonly GccV2PlaywrightBrowserHolder _holder;
    private readonly ILogger<GccV2PlaywrightStartupHostedService> _logger;

    public GccV2PlaywrightStartupHostedService(
        GccV2PlaywrightBrowserHolder holder,
        ILogger<GccV2PlaywrightStartupHostedService> logger)
    {
        _holder = holder;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _holder.InitializeAsync();
            if (_holder.Browser is null)
                _logger.LogWarning("Content Creator Playwright browser not available after startup; hierarchy crawl will soft-fail until retry.");
            else
                _logger.LogInformation("Content Creator Playwright Chromium ready for mobile hierarchy crawl.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content Creator Playwright startup failed; hierarchy crawl soft-fails.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
