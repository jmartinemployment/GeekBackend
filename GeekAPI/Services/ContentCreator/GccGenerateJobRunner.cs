using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Starts a Generate run on a background Task with its own DI scope so the HTTP request can return
/// 202 immediately. Mirrors ToolsGenerationJobRunner, the established pattern for this hub.
///
/// Generate is minutes of work -- partner extraction plus a multi-call write, once per selected
/// content type -- which is longer than any gateway between the browser and here will hold a
/// request open. See plans/generate-async-signalr.md.
/// </summary>
public sealed class GccGenerateJobRunner
{
    private readonly GccJobStore _jobs;
    private readonly GccGenerateNotifier _notifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GccGenerateJobRunner> _logger;

    public GccGenerateJobRunner(
        GccJobStore jobs,
        GccGenerateNotifier notifier,
        IServiceScopeFactory scopeFactory,
        ILogger<GccGenerateJobRunner> logger)
    {
        _jobs = jobs;
        _notifier = notifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <param name="ownerUserId">
    /// Captured from the request before going async. Nothing in the background scope can read
    /// HttpContext, and the hub authorises a join against this value.
    /// </param>
    public GccJob Start(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string>? outputTypes,
        string? mustMentionBlock,
        string ownerUserId)
    {
        var job = _jobs.Create("generate", create.Id, ownerUserId);
        _ = PushJobAsync(job.Id);
        _ = Task.Run(() => RunAsync(job.Id, create, section, provider, outputTypes, mustMentionBlock));
        return job;
    }

    private async Task RunAsync(
        Guid jobId,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string>? outputTypes,
        string? mustMentionBlock)
    {
        try
        {
            // A new scope, because the request's scoped services (repository, generate service,
            // coordinator) are disposed the moment the 202 is written.
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<HttpGccRepository>();
            var gen = scope.ServiceProvider.GetRequiredService<GccGenerateService>();
            var coordinator = scope.ServiceProvider.GetRequiredService<GccGenerationCoordinator>();

            // CancellationToken.None deliberately. The request's token is cancelled as soon as the
            // 202 returns, so passing it here would abort generation instantly and leave a job
            // stuck "running" with nothing to show for it -- the silent-failure shape this whole
            // change exists to remove.
            var result = await coordinator.RunGenerateAsync(
                repo, gen, create, section, provider, outputTypes, mustMentionBlock,
                CancellationToken.None,
                onTypeCompleted: (contentType, produced) =>
                    _notifier.PushTypeAsync(jobId, contentType, produced, error: null));

            _jobs.Complete(jobId, result);
            await PushJobAsync(jobId);
        }
        catch (Exception ex)
        {
            // Recorded, pushed and visible. A background failure that only ever reached a log is
            // how a total extraction outage read as a partner-data shortage for two hours.
            _logger.LogError(ex, "Generate job {JobId} failed for create {CreateId}", jobId, create.Id);
            _jobs.Fail(jobId, $"{ex.GetType().Name}: {ex.Message}");
            await PushJobAsync(jobId);
        }
    }

    private async Task PushJobAsync(Guid jobId)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return;
        try
        {
            await _notifier.PushJobAsync(job);
        }
        catch (Exception ex)
        {
            // A push failure must not take the job down with it -- the job state is still correct
            // and GetJob still serves it on reconnect.
            _logger.LogWarning(ex, "Could not push generate job {JobId} to the hub", jobId);
        }
    }
}
