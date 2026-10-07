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
        _ = Task.Run(() => RunAsync(job.Id, create, section, provider, outputTypes, mustMentionBlock, row: null));
        return job;
    }

    /// <summary>
    /// Start a project run whose row GeekRepository has already written as running. The hub, the row
    /// and the in-process entry share the row's id, every version written is stamped with the brief
    /// revision the row recorded (J7), and the row is finished ready or failed with the job.
    /// </summary>
    /// <param name="view">The create the coordinator writes under, carrying the project's brief and
    /// keyword -- see <c>GccProjectsController.Generate</c>.</param>
    public GccJob StartForProject(
        GccGenerateJobDto row,
        GccCreateDto view,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string> outputTypes,
        string? mustMentionBlock,
        string ownerUserId)
    {
        var job = _jobs.Create("generate", row.CreateId, ownerUserId, row.Id, row.ProjectId);
        _ = PushJobAsync(job.Id);
        _ = Task.Run(() => RunAsync(job.Id, view, section, provider, outputTypes, mustMentionBlock, row));
        return job;
    }

    private async Task RunAsync(
        Guid jobId,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string>? outputTypes,
        string? mustMentionBlock,
        GccGenerateJobDto? row)
    {
        // A new scope, because the request's scoped services (repository, generate service,
        // coordinator) are disposed the moment the 202 is written. Opened inside the try, so a scope
        // that cannot be built fails the job rather than leaving it running.
        IServiceScope? scope = null;
        HttpGccRepository? repo = null;
        var briefRevision = row is null ? null : new GccBriefRevisionStamp(row.BriefRevisionId, row.BriefRevisionSavedAtUtc);
        try
        {
            scope = _scopeFactory.CreateScope();
            repo = scope.ServiceProvider.GetRequiredService<HttpGccRepository>();
            var gen = scope.ServiceProvider.GetRequiredService<GccGenerateService>();
            var coordinator = scope.ServiceProvider.GetRequiredService<GccGenerationCoordinator>();

            // The run's record, written to the job row's events as the run goes (GccRunLog). Only a
            // run that has a row can have a record; the create-keyed path has none.
            if (row is not null)
            {
                var sinkRepo = repo;
                var log = GccRunLog.Begin(jobId, (events, ct) => sinkRepo.AppendGenerateJobEventsAsync(jobId, events, ct), _logger);
                await log.RecordAsync("started", new
                {
                    projectId = row.ProjectId,
                    createId = create.Id,
                    provider = provider.ToString(),
                    requestedTypes = outputTypes ?? [],
                    briefRevisionId = row.BriefRevisionId,
                    topic = create.Topic,
                });
            }

            // CancellationToken.None deliberately. The request's token is cancelled as soon as the
            // 202 returns, so passing it here would abort generation instantly and leave a job
            // stuck "running" with nothing to show for it -- the silent-failure shape this whole
            // change exists to remove.
            var result = await coordinator.RunGenerateAsync(
                repo, gen, create, section, provider, outputTypes, mustMentionBlock,
                CancellationToken.None,
                onTypeOutcome: (contentType, produced, error) =>
                    BestEffortAsync(jobId, "type outcome", () => _notifier.PushTypeAsync(jobId, contentType, produced, error)),
                onReadiness: (contentType, partners) =>
                    BestEffortAsync(jobId, "pre-flight", () => _notifier.PushPreflightAsync(jobId, contentType, partners)),
                onTypeWarning: (contentType, warning) =>
                    BestEffortAsync(jobId, "warning", () => _notifier.PushWarningAsync(jobId, contentType, warning)),
                briefRevision: briefRevision);

            _jobs.Complete(jobId, result);
            if (GccRunLog.Current is { } completed)
            {
                await completed.RecordAsync("completed", new
                {
                    elapsedMs = completed.ElapsedMs,
                    events = completed.Events + 1,
                    recordingFailures = completed.RecordingFailures,
                });
            }
            if (row is not null)
                await RecordAsync(jobId, () => repo.CompleteGenerateJobAsync(jobId, _jobs.Get(jobId)!.ResultJson ?? "null"));
            await PushJobAsync(jobId);
        }
        catch (Exception ex)
        {
            // Recorded, pushed and visible. A background failure that only ever reached a log is
            // how a total extraction outage read as a partner-data shortage for two hours.
            _logger.LogError(ex, "Generate job {JobId} failed for create {CreateId}", jobId, create.Id);
            var error = $"{ex.GetType().Name}: {ex.Message}";
            if (GccRunLog.Current is { } failed)
            {
                await failed.RecordAsync("failure", new
                {
                    error,
                    fault = GccRunFault.Describe(ex),
                    elapsedMs = failed.ElapsedMs,
                    events = failed.Events + 1,
                    recordingFailures = failed.RecordingFailures,
                });
            }
            _jobs.Fail(jobId, error);
            if (row is not null && repo is not null)
                await RecordAsync(jobId, () => repo.FailGenerateJobAsync(jobId, error));
            await PushJobAsync(jobId);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// Finish the project run's row. A row that cannot be written stays running until the next
    /// startup fails it, and blocks a second Generate on the project by name meanwhile -- logged as an
    /// error, visible, never a silent success.
    /// </summary>
    private async Task RecordAsync(Guid jobId, Func<Task<GccGenerateJobDto?>> write)
    {
        try
        {
            if (await write() is null)
                _logger.LogError("Generate job {JobId} had no running row to finish", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record how generate job {JobId} ended; its row stays running", jobId);
        }
    }

    /// <summary>
    /// A live push to the page, which can fail without the run having done anything wrong. Some of these
    /// run after the pages were saved, so one that threw would fail a job whose work was kept; the run
    /// goes on. The failure is not quiet: it is logged as an error and written into the run's record, where
    /// the Run log shows it. The job row, and GetJob on reconnect, still carry the truth.
    /// </summary>
    private async Task BestEffortAsync(Guid jobId, string what, Func<Task> push)
    {
        try
        {
            await push();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not push the {What} of generate job {JobId} to the hub", what, jobId);
            await GccRunLog.RecordIfAnyAsync("warning", new { step = "hub push", what, fault = GccRunFault.Describe(ex) });
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
