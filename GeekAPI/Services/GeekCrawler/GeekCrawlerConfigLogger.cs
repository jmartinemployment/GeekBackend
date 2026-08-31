namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerConfigLogger : IHostedService
{
    private readonly GeekCrawlerOptions _options;
    private readonly ILogger<GeekCrawlerConfigLogger> _logger;

    public GeekCrawlerConfigLogger(GeekCrawlerOptions options, ILogger<GeekCrawlerConfigLogger> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Geek-Crawler config: mode={Mode}, workerCount={WorkerCount}, parallelismPerOrigin={ParallelismPerOrigin}, hostDelaySeconds={HostDelaySeconds}",
            _options.Mode,
            _options.WorkerCount,
            _options.ParallelismPerOrigin,
            _options.HostDelaySeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
