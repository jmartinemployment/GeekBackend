using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Fails every project Generate run still marked running, once, when GeekAPI starts.
/// </summary>
/// <remarks>
/// <para>
/// A run executes on a background task inside this process. A redeploy ends the process, so a run
/// still running at startup was cut off and will never finish: left as it is it reads "running"
/// forever, and it holds the project's one-running-run slot, so the operator could not start another.
/// This is the one-shot orphan recovery AGENTS.md asks for on startup -- not a poller.
/// </para>
/// <para>
/// One GeekAPI instance is assumed, as the in-process job store already assumes. With two, one
/// instance's startup would fail the other's running runs; that needs an instance id on the row before
/// a second instance is added.
/// </para>
/// <para>
/// A repository that cannot be reached is logged as an error and startup continues: refusing to start
/// the gateway over a job table would take every other route down with it. The rows stay running, and
/// a Generate on one of their projects is refused naming the run, so the state is visible, not silent.
/// </para>
/// </remarks>
public sealed class GccInterruptedJobsOnStartup(
    IServiceScopeFactory scopeFactory,
    ILogger<GccInterruptedJobsOnStartup> logger) : IHostedService
{
    internal const string InterruptedError = "interrupted by redeploy: GeekAPI restarted while this run was running";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccRepository>();
            var failed = await repo.FailInterruptedGenerateJobsAsync(InterruptedError, cancellationToken);
            if (failed > 0)
                logger.LogWarning("Marked {Count} generate run(s) failed: {Reason}", failed, InterruptedError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not fail interrupted generate runs at startup; they remain marked running.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
