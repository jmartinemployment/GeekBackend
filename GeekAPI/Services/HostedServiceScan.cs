namespace GeekAPI.Services;

/// <summary>
/// Shared filter for BackgroundService scan loops: swallow HttpClient timeouts
/// (TaskCanceledException) but still let host shutdown cancellation propagate.
/// </summary>
internal static class HostedServiceScan
{
    /// <summary>
    /// Returns true when the exception should be logged and the scan loop continued.
    /// Returns false when the exception should escape (host is stopping).
    /// </summary>
    public static bool ShouldLogAndContinue(Exception ex, CancellationToken stoppingToken) =>
        ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested;
}
