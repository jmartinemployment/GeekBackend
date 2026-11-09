namespace GeekAPI.Services.Social.Orchestration;

/// <summary>One step the orchestrator could not complete, kept so an operator can read it. It holds a step name and a failure code, never an event's text.</summary>
public sealed record SocialStepFailure(string Step, string Code, DateTime At);

/// <summary>What the orchestrator has done since it started, for the operator view Phase 5 adds.</summary>
public sealed record SocialOrchestratorSnapshot(
    DateTime? RecoveredAt,
    int EventsWokenAtStartup,
    int InterruptedSendsFailed,
    int EventsScrubbed,
    int EventsTakenIn,
    IReadOnlyList<SocialStepFailure> Failures);

/// <summary>
/// The orchestrator's own record of what it did and what failed. A failed step is logged and also
/// kept here, because a log line nobody reads is not an operator seeing it.
/// </summary>
public sealed class SocialOrchestratorStatus
{
    private const int FailuresKept = 20;

    private readonly object _gate = new();
    private readonly List<SocialStepFailure> _failures = new();
    private DateTime? _recoveredAt;
    private int _wokenAtStartup;
    private int _interruptedFailed;
    private int _scrubbed;
    private int _takenIn;

    public void RecordRecovered(DateTime at, int eventsWoken, int interruptedSendsFailed)
    {
        lock (_gate)
        {
            _recoveredAt = at;
            _wokenAtStartup = eventsWoken;
            _interruptedFailed = interruptedSendsFailed;
        }
    }

    public void RecordScrubbed(int events)
    {
        lock (_gate) _scrubbed += events;
    }

    public void RecordEventTakenIn()
    {
        lock (_gate) _takenIn++;
    }

    public void RecordFailure(string step, string code, DateTime at)
    {
        lock (_gate)
        {
            _failures.Add(new SocialStepFailure(step, code, at));
            if (_failures.Count > FailuresKept)
                _failures.RemoveAt(0);
        }
    }

    public SocialOrchestratorSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new SocialOrchestratorSnapshot(
                _recoveredAt, _wokenAtStartup, _interruptedFailed, _scrubbed, _takenIn, _failures.ToArray());
        }
    }
}
