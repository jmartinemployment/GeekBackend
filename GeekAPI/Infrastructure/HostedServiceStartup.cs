namespace GeekAPI.Infrastructure;

/// <summary>
/// OpenIddict seeders call GeekRepository over HTTP with a machine token from /connect/token.
/// That endpoint is hosted by this same process, so seeding must run after Kestrel is listening.
/// </summary>
internal static class HostedServiceStartup
{
    public static async Task RunAfterApplicationStartedAsync(
        IHostApplicationLifetime lifetime,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
        await action(cancellationToken);
    }
}
