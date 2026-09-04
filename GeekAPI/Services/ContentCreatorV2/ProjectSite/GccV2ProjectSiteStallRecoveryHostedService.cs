using GeekAPI.HttpClients;
using GeekAPI.Services;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

/// <summary>Periodically resets stalled project-site crawls back to pending.</summary>
public sealed class GccV2ProjectSiteStallRecoveryHostedService : BackgroundService
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GccV2ProjectSiteCrawlWake _wake;
    private readonly ILogger<GccV2ProjectSiteStallRecoveryHostedService> _logger;

    public GccV2ProjectSiteStallRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        GccV2ProjectSiteCrawlWake wake,
        ILogger<GccV2ProjectSiteStallRecoveryHostedService> logger)
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
            catch (Exception ex) when (HostedServiceScan.ShouldLogAndContinue(ex, stoppingToken))
            {
                _logger.LogError(ex, "Project-site crawl stall recovery scan failed.");
            }
        }
    }

    internal async Task ScanStalledRunsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var now = DateTimeOffset.UtcNow;

        var running = await repo.GetProjectSiteCrawlRunsByStatusAsync("running", limit: 200, ct)
            .ConfigureAwait(false);
        var recovered = 0;

        foreach (var run in running)
        {
            var activity = await repo.GetProjectSiteCrawlPageActivityAsync(run.Id, ct).ConfigureAwait(false);
            if (activity is null)
                continue;

            var shouldRecover = activity.PageCount == 0
                ? ShouldRecoverRunningOrphan(run, now)
                : activity.LastCrawledAtUtc is DateTimeOffset last
                    && ShouldRecoverStalledRunning(run, now, last);

            if (!shouldRecover)
                continue;

            await repo.PatchProjectSiteCrawlRunAsync(
                run.Id,
                new PatchGccV2ProjectSiteCrawlRunCommand(Status: "pending"),
                ct).ConfigureAwait(false);
            _wake.Wake(run.Id);
            recovered++;
        }

        if (recovered > 0)
        {
            _logger.LogInformation(
                "Stall recovery reset and woke {Count} project-site crawl run(s) to pending.",
                recovered);
        }
    }

    private static bool ShouldRecoverRunningOrphan(GccV2ProjectSiteCrawlRunDto run, DateTimeOffset now) =>
        string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase)
        && run.StartedAtUtc is not null
        && now - run.StartedAtUtc.Value >= GeekCrawlerRecovery.PendingWakeGrace;

    private static bool ShouldRecoverStalledRunning(
        GccV2ProjectSiteCrawlRunDto run,
        DateTimeOffset now,
        DateTimeOffset lastPageCrawledAtUtc) =>
        string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase)
        && now - lastPageCrawledAtUtc >= GeekCrawlerRecovery.StalledRunningGrace;
}
