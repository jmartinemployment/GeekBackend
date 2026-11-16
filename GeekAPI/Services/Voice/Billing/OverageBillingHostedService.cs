namespace GeekAPI.Services.Voice.Billing;

/// <summary>
/// Runs end-of-cycle billing (<see cref="OverageBilling"/>) once an hour. A sweep that throws is logged at Error and the next hour runs
/// again; every step inside a sweep records or logs its own outcome, so nothing a sweep leaves undone is silent.
/// </summary>
public sealed class OverageBillingHostedService : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<OverageBillingHostedService> _logger;

    public OverageBillingHostedService(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<OverageBillingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OverageBilling>().RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (HostedServiceScan.ShouldLogAndContinue(ex, stoppingToken))
            {
                _logger.LogError(ex, "End-of-cycle billing sweep failed.");
            }
        }
    }
}
