namespace GeekAPI.Infrastructure;

/// <summary>
/// OpenIddict seeders call GeekRepository over HTTP with a machine token from /connect/token.
/// That endpoint is hosted by this same process, so seeding must run after Kestrel is listening.
/// </summary>
internal static class HostedServiceStartup
{
    /// <summary>
    /// Must return immediately from <see cref="IHostedService.StartAsync"/> — awaiting
    /// <see cref="IHostApplicationLifetime.ApplicationStarted"/> inside StartAsync deadlocks host startup.
    /// </summary>
    public static Task ScheduleAfterApplicationStartedAsync(
        IHostApplicationLifetime lifetime,
        Func<CancellationToken, Task> action,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            _ = RunAsync(action, logger, cancellationToken);
        });

        return Task.CompletedTask;
    }

    private static async Task RunAsync(
        Func<CancellationToken, Task> action,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenIddict startup seeding failed.");
        }
    }
}
