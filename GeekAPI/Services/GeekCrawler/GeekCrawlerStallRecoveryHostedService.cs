using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Periodically resets running runs stalled beyond the grace window back to pending.</summary>
public sealed class GeekCrawlerStallRecoveryHostedService : BackgroundService
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GeekCrawlerWake _wake;
    private readonly ILogger<GeekCrawlerStallRecoveryHostedService> _logger;

    public GeekCrawlerStallRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        GeekCrawlerWake wake,
        ILogger<GeekCrawlerStallRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _wake = wake;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ScanStalledRunsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Geek-Crawler stall recovery scan failed.");
            }
        }
    }

    internal async Task ScanStalledRunsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGeekCrawlerRepository>();
        var now = DateTimeOffset.UtcNow;

        var running = await repo.GetRunsByStatusAsync("running", limit: 200, ct).ConfigureAwait(false);
        var recovered = 0;

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
            recovered++;
        }

        if (recovered > 0)
        {
            _logger.LogInformation(
                "Stall recovery reset and woke {Count} Geek-Crawler run(s) to pending.",
                recovered);
        }
    }
}
