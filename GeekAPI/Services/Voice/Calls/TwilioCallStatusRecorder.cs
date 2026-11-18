using System.Globalization;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>What a status post came to: the call it recorded, or nothing because the status is not one a call ends in.</summary>
public sealed record TwilioCallRecorded(string CallStatus, Guid? CallId);

/// <summary>
/// Records a tenant's call from Twilio's status callback (docs/Geek-Receptionist.md section 5), posted to the tenant's call-status
/// route once the call has ended and only after <see cref="TenantTwilioWebhook"/> has accepted it. The row is keyed by the Twilio call
/// SID, so a repeated post changes nothing. It holds what billing needs and nothing of what was said: no caller number, transcript or
/// recording (item 6); the form's From and To are never read. Billable minutes are the call's length in seconds (CallDuration) rounded
/// up to the whole minute, per call; they are what end-of-cycle billing adds up. The call ended at the post's Timestamp when Twilio
/// sends one, and otherwise at the moment the post arrived; it started that many seconds earlier. Nothing is guessed: a completed call
/// sent without a length, or with a length or time that does not parse, is refused, not recorded as zero minutes.
/// </summary>
public sealed class TwilioCallStatusRecorder
{
    /// <summary>The statuses a call ends in (Twilio's CallStatus values). Any other is accepted and not recorded.</summary>
    public static readonly IReadOnlySet<string> FinalStatuses = new HashSet<string>(StringComparer.Ordinal) { "completed", "busy", "failed", "no-answer", "canceled" };

    public const string Completed = "completed";

    private readonly IVoiceStore _store;
    private readonly ILogger<TwilioCallStatusRecorder> _logger;

    public TwilioCallStatusRecorder(IVoiceStore store, ILogger<TwilioCallStatusRecorder> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Billable minutes for a call of this many seconds: every started minute counts.</summary>
    public static int BillableMinutes(long durationSeconds) => durationSeconds <= 0 ? 0 : (int)((durationSeconds + 59) / 60);

    public async Task<Result<TwilioCallRecorded>> RecordAsync(Guid tenantId, IReadOnlyDictionary<string, string> form, DateTimeOffset receivedAt, CancellationToken ct = default)
    {
        var callSid = Text(form, "CallSid");
        var status = Text(form, "CallStatus");
        if (callSid is null || status is null) return Invalid("The post is missing its CallSid or CallStatus.");
        if (!FinalStatuses.Contains(status)) return Result<TwilioCallRecorded>.Success(new TwilioCallRecorded(status, null));

        long duration = 0;
        var durationText = Text(form, "CallDuration");
        if (durationText is null)
        {
            if (status == Completed) return Invalid("Twilio sent a completed call with no CallDuration.");
        }
        else if (!long.TryParse(durationText, NumberStyles.None, CultureInfo.InvariantCulture, out duration))
        {
            return Invalid("The CallDuration is not a whole number of seconds.");
        }

        var ended = receivedAt;
        var timestamp = Text(form, "Timestamp");
        if (timestamp is not null && !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out ended))
            return Invalid("The Timestamp is not a date.");

        var command = new UpsertVoiceCallCommand(
            TwilioCallSid: callSid,
            CallStatus: status,
            StartedAt: ended.AddSeconds(-duration),
            EndedAt: ended,
            DurationSeconds: (int)Math.Min(duration, int.MaxValue),
            BillableMinutes: BillableMinutes(duration),
            Disposition: null);

        var stored = await _store.UpsertCallAsync(tenantId, command, ct).ConfigureAwait(false);
        if (!stored.IsSuccess) return Result<TwilioCallRecorded>.Failure(stored.Error!, stored.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        _logger.LogInformation("Tenant {TenantId}: Twilio call {CallSid} ({Status}) recorded as call {CallId}, {Minutes} billable minutes.",
            tenantId, callSid, status, stored.Value!.CallId, command.BillableMinutes);
        return Result<TwilioCallRecorded>.Success(new TwilioCallRecorded(status, stored.Value.CallId));
    }

    private static Result<TwilioCallRecorded> Invalid(string message) => Result<TwilioCallRecorded>.Failure(message, TenantCallFailureCodes.InvalidCall);

    private static string? Text(IReadOnlyDictionary<string, string> form, string name) =>
        form.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
