using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>One step the orchestrator could not complete, kept so an operator can read it. It holds a step name and a failure code, never an event's text.</summary>
public sealed record SocialStepFailure(string Step, string Code, DateTime At);

/// <summary>What the orchestrator has done since it started, for the operator view Phase 5 adds.</summary>
public sealed record SocialOrchestratorSnapshot(
    DateTime? RecoveredAt,
    int EventsRecoveredAtStartup,
    int InterruptedSendsFailed,
    int EventsScrubbed,
    int EventsTakenIn,
    int CommentsReplyNeeded,
    int CommentsNoReplyNeeded,
    int CommentsBlockedNoClearance,
    int CommentsFailed,
    int DraftsPending,
    int DraftsForAPerson,
    int DraftsRefused,
    int DraftsAutoApproved,
    int DraftsSent,
    int DraftsSendFailed,
    int SendsHeld,
    IReadOnlyList<SocialStepFailure> Failures);

/// <summary>Which kind of draft the orchestrator stored for a comment.</summary>
public enum SocialDraftKind
{
    /// <summary>Drafted and passed by the Reviewer; waits for a person to approve, edit or reject it.</summary>
    Pending,

    /// <summary>Triage said a person must write the reply; the draft carries the reason and no text.</summary>
    ForAPerson,

    /// <summary>The Reviewer refused the drafted text; stored rejected with the rules it broke.</summary>
    Refused,
}

/// <summary>Where one send attempt ended.</summary>
public enum SocialSendKind
{
    /// <summary>The platform confirmed it and the draft is sent.</summary>
    Sent,

    /// <summary>The send was made and failed, or failed with an unknown outcome; the draft is failed.</summary>
    Failed,

    /// <summary>A limit, a pause or the token kept an approved draft back; nothing was sent and it stays approved.</summary>
    Held,
}

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
    private int _recoveredAtStartup;
    private int _interruptedFailed;
    private int _scrubbed;
    private int _takenIn;
    private int _replyNeeded;
    private int _noReplyNeeded;
    private int _blocked;
    private int _failed;
    private int _draftsPending;
    private int _draftsForAPerson;
    private int _draftsRefused;
    private int _autoApproved;
    private int _sent;
    private int _sendFailed;
    private int _held;

    public void RecordRecovered(DateTime at, int eventsRecovered, int interruptedSendsFailed)
    {
        lock (_gate)
        {
            _recoveredAt = at;
            _recoveredAtStartup = eventsRecovered;
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

    /// <summary>Counts a triage outcome the store accepted. An outcome the code does not know is a bug and throws.</summary>
    public void RecordOutcome(string outcome)
    {
        lock (_gate)
        {
            switch (outcome)
            {
                case TriageOutcomes.ReplyNeeded: _replyNeeded++; break;
                case TriageOutcomes.NoReplyNeeded: _noReplyNeeded++; break;
                case TriageOutcomes.BlockedNoClearance: _blocked++; break;
                case TriageOutcomes.Failed: _failed++; break;
                default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a stored triage outcome.");
            }
        }
    }

    public void RecordDraft(SocialDraftKind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case SocialDraftKind.Pending: _draftsPending++; break;
                case SocialDraftKind.ForAPerson: _draftsForAPerson++; break;
                case SocialDraftKind.Refused: _draftsRefused++; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a draft kind.");
            }
        }
    }

    public void RecordAutoApproved()
    {
        lock (_gate) _autoApproved++;
    }

    public void RecordSend(SocialSendKind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case SocialSendKind.Sent: _sent++; break;
                case SocialSendKind.Failed: _sendFailed++; break;
                case SocialSendKind.Held: _held++; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a send kind.");
            }
        }
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
                _recoveredAt, _recoveredAtStartup, _interruptedFailed, _scrubbed, _takenIn,
                _replyNeeded, _noReplyNeeded, _blocked, _failed,
                _draftsPending, _draftsForAPerson, _draftsRefused,
                _autoApproved, _sent, _sendFailed, _held,
                _failures.ToArray());
        }
    }
}
