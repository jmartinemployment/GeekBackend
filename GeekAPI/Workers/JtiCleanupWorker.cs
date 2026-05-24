using GeekApplication.Interfaces;

namespace GeekAPI.Workers;

public sealed class JtiCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<JtiCleanupWorker> _logger;

    public JtiCleanupWorker(IServiceProvider services, ILogger<JtiCleanupWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var tokens = scope.ServiceProvider.GetRequiredService<IOAuthTokenRepository>();
                var result = await tokens.CleanupExpiredBlacklistEntriesAsync();
                if (result.IsSuccess)
                    _logger.LogInformation("JTI blacklist cleanup completed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JTI blacklist cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
