using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>
/// The orchestrator's intake: it is woken with the id of a stored event, loads it, and runs the
/// retention purge. It never calls Claude and never calls a platform, it reaches nothing but
/// <see cref="ISocialStore"/>, and it keeps no state the store does not also hold. A wake lost with
/// a restart is found again at startup, because recovery wakes every comment event Triage never decided.
/// </summary>
/// <remarks>
/// Phase 3 stops at taking the event in. Triage, the drafter and the Reviewer arrive in Phase 4a and
/// continue from <see cref="TakeInAsync"/>; until then a comment event stays untriaged on purpose, so
/// nothing is decided that no agent decided. There is no timer (AGENTS.md forbids polling), so
/// recovery that could not finish at startup, because the store was not up yet, runs again at the
/// next wake instead.
/// </remarks>
public sealed class SocialOrchestrator : BackgroundService
{
    /// <summary>How many untriaged comments one recovery pass reads. Phase 4a loops until the store has none left.</summary>
    internal const int RecoveryBatch = 500;

    private readonly SocialWake _wake;
    private readonly IServiceScopeFactory _scopes;
    private readonly SocialOrchestratorStatus _status;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialOrchestrator> _logger;
    private bool _recovered;

    public SocialOrchestrator(
        SocialWake wake,
        IServiceScopeFactory scopes,
        SocialOrchestratorStatus status,
        TimeProvider clock,
        ILogger<SocialOrchestrator> logger)
    {
        _wake = wake;
        _scopes = scopes;
        _status = status;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SocialOrchestrator starting.");
        try
        {
            await RecoverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // A recovery that throws must not stop the host; it is recorded and tried again at the next wake.
            _status.RecordFailure("recover at startup", "unexpected_error", _clock.GetUtcNow().UtcDateTime);
            _logger.LogError(ex, "SocialOrchestrator could not finish its startup recovery; it will try again at the next wake.");
        }

        await foreach (var eventId in _wake.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                // Recovery that failed at startup (the store was not up yet) is tried again here.
                if (!_recovered)
                    await RecoverAsync(stoppingToken).ConfigureAwait(false);

                await TakeInAsync(eventId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.RecordFailure("take in an event", "unexpected_error", _clock.GetUtcNow().UtcDateTime);
                _logger.LogError(ex, "SocialOrchestrator failed on event {EventId}; it stays untriaged and is found again at startup.", eventId);
            }
        }
    }

    /// <summary>
    /// Fails any send a restart interrupted, scrubs text past its retention, and wakes every comment
    /// no agent has decided. Every step is tried even when an earlier one failed, and the pass counts
    /// as recovered only when all of them did.
    /// </summary>
    public async Task RecoverAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var allSucceeded = true;
        var interrupted = 0;
        var woken = 0;

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        var sends = await store.FailInterruptedSendsAsync(now, ct).ConfigureAwait(false);
        if (sends.IsSuccess)
            interrupted = sends.Value!.DraftIds.Count;
        else
            allSucceeded &= Fail("fail interrupted sends", sends.ErrorCode, sends.Error, now);

        allSucceeded &= await PurgeAsync(store, now, ct).ConfigureAwait(false);

        var untriaged = await store.ListEventsAsync(TriageOutcomes.Untriaged, null, RecoveryBatch, ct).ConfigureAwait(false);
        if (untriaged.IsSuccess)
        {
            foreach (var pending in untriaged.Value!)
            {
                _wake.Wake(pending.Id);
                woken++;
            }
        }
        else
        {
            allSucceeded &= Fail("list untriaged events", untriaged.ErrorCode, untriaged.Error, now);
        }

        _recovered = allSucceeded;
        _status.RecordRecovered(now, woken, interrupted);
        _logger.LogInformation(
            "SocialOrchestrator recovery: {Interrupted} interrupted send(s) failed, {Woken} untriaged event(s) woken, complete: {Complete}.",
            interrupted, woken, allSucceeded);
    }

    /// <summary>Loads the event the Listener stored and then purges what has outlived its retention.</summary>
    public async Task TakeInAsync(Guid eventId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        var loaded = await store.GetEventAsync(eventId, ct).ConfigureAwait(false);
        if (loaded.IsSuccess)
            _status.RecordEventTakenIn();
        else
            Fail("load an event", loaded.ErrorCode, loaded.Error, now);

        await PurgeAsync(store, now, ct).ConfigureAwait(false);
    }

    private async Task<bool> PurgeAsync(ISocialStore store, DateTime now, CancellationToken ct)
    {
        var purged = await store.PurgeExpiredAsync(now, ct).ConfigureAwait(false);
        if (!purged.IsSuccess)
            return Fail("purge expired text", purged.ErrorCode, purged.Error, now);

        _status.RecordScrubbed(purged.Value!.EventsScrubbed);
        return true;
    }

    private bool Fail(string step, string? code, string? error, DateTime at)
    {
        _status.RecordFailure(step, code ?? "unknown", at);
        _logger.LogError("SocialOrchestrator could not {Step}: {Code}. {Error}", step, code, error);
        return false;
    }
}
