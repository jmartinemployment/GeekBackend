using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Enqueues due crawl schedules on a fixed interval.</summary>
public sealed class GeekCrawlerScheduleHostedService : BackgroundService
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeekCrawlerScheduleHostedService> _logger;

    public GeekCrawlerScheduleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<GeekCrawlerScheduleHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (HostedServiceScan.ShouldLogAndContinue(ex, stoppingToken))
            {
                _logger.LogError(ex, "Geek-Crawler schedule scan failed.");
            }
        }
    }

    internal async Task ProcessDueSchedulesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGeekCrawlerRepository>();
        var crawler = scope.ServiceProvider.GetRequiredService<GeekCrawlerService>();
        var now = DateTimeOffset.UtcNow;

        var due = await repo.ListDueSchedulesAsync(now, limit: 50, ct).ConfigureAwait(false);
        foreach (var schedule in due)
        {
            try
            {
                var seeds = JsonSerializer.Deserialize<List<string>>(schedule.SeedUrlsJson, JsonOpts) ?? [];
                if (seeds.Count == 0)
                {
                    _logger.LogWarning("Schedule {ScheduleId} has no seeds; disabling.", schedule.Id);
                    await repo.PatchScheduleAsync(
                        schedule.Id,
                        new PatchGeekCrawlerScheduleCommand(Enabled: false),
                        ct).ConfigureAwait(false);
                    continue;
                }

                var claimed = await repo.ClaimScheduleAsync(
                    schedule.Id,
                    new ClaimGeekCrawlerScheduleCommand(
                        schedule.NextRunUtc,
                        now.AddHours(schedule.IntervalHours),
                        now),
                    ct).ConfigureAwait(false);

                if (claimed is null)
                    continue;

                var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(seeds);
                var run = await crawler.StartCrawlAsync(
                    schedule.OwnerUserId,
                    schedule.CrawlType,
                    normalized,
                    ct).ConfigureAwait(false);

                await repo.PatchScheduleAsync(
                    schedule.Id,
                    new PatchGeekCrawlerScheduleCommand(LastRunId: run.Id),
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Started scheduled Geek-Crawler run {RunId} for schedule {ScheduleId}.",
                    run.Id,
                    schedule.Id);
            }
            catch (Exception ex) when (HostedServiceScan.ShouldLogAndContinue(ex, ct))
            {
                _logger.LogError(ex, "Failed to start scheduled crawl for schedule {ScheduleId}.", schedule.Id);
            }
        }
    }
}
