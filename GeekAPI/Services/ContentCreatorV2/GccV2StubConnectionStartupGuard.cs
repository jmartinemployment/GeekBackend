namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Validates stub-connection env at process start (see <see cref="GccV2StubConnectionPolicy"/>).</summary>
public sealed class GccV2StubConnectionStartupGuard(ILogger<GccV2StubConnectionStartupGuard> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        GccV2StubConnectionPolicy.ValidateAtStartup(logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
