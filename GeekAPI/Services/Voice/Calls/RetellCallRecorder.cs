using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>Why a Retell delivery was not recorded. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class RetellCallFailureCodes
{
    /// <summary>The delivery is not a call Retell documents: a field it always sends is missing or of another kind.</summary>
    public const string InvalidCall = "invalid_call";
    /// <summary>The call's agent is not the agent this tenant's number answers with.</summary>
    public const string AgentMismatch = "agent_mismatch";
}

/// <summary>What a delivery came to: the call it recorded, or nothing because the event is not one a call is recorded from.</summary>
public sealed record RetellCallRecorded(string Event, Guid? CallId);

/// <summary>
/// Records a tenant's call from Retell's webhook (docs/Geek-Receptionist.md section 5): <c>call_ended</c> brings the call with its
/// length, and <c>call_analyzed</c> brings it again with Retell's summary. Both write the same row, keyed by Retell's call id, so a
/// repeated delivery changes nothing. Billable minutes are the call's length rounded up to the whole minute, per call; they are what
/// end-of-cycle billing adds up. The call must come from the agent this tenant's number answers with, so one tenant's route cannot
/// record another tenant's call. Other events are accepted and not recorded. Nothing is guessed: a call Retell says ended but sends
/// without a length is refused, not recorded as zero minutes.
/// </summary>
public sealed class RetellCallRecorder
{
    public static readonly IReadOnlySet<string> RecordedEvents = new HashSet<string>(StringComparer.Ordinal) { "call_ended", "call_analyzed" };

    /// <summary>Retell's status for a call that never reached the agent; it has no length and no minutes.</summary>
    public const string NotConnected = "not_connected";

    private readonly IVoiceStore _store;
    private readonly ILogger<RetellCallRecorder> _logger;

    public RetellCallRecorder(IVoiceStore store, ILogger<RetellCallRecorder> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Billable minutes for a call of this many milliseconds: every started minute counts.</summary>
    public static int BillableMinutes(long durationMs) => durationMs <= 0 ? 0 : (int)((durationMs + 59_999) / 60_000);

    public async Task<Result<RetellCallRecorded>> RecordAsync(Guid tenantId, JsonElement delivery, CancellationToken ct = default)
    {
        var evt = Text(delivery, "event");
        if (evt is null) return Invalid("The delivery names no event.");
        if (!RecordedEvents.Contains(evt)) return Result<RetellCallRecorded>.Success(new RetellCallRecorded(evt, null));

        if (!delivery.TryGetProperty("call", out var call) || call.ValueKind != JsonValueKind.Object) return Invalid("The delivery carries no call.");

        var callId = Text(call, "call_id");
        var agentId = Text(call, "agent_id");
        var caller = Text(call, "from_number");
        var status = Text(call, "call_status");
        var started = Long(call, "start_timestamp");
        if (callId is null || agentId is null || caller is null || status is null || started is null)
            return Invalid("The call is missing its id, agent, caller, status or start time.");

        var duration = Long(call, "duration_ms");
        if (duration is null && status != NotConnected) return Invalid($"Retell sent {evt} for a {status} call with no length.");
        var ended = Long(call, "end_timestamp");

        var telephony = await _store.GetTelephonyAsync(tenantId, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess) return Result<RetellCallRecorded>.Failure(telephony.Error!, telephony.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (!string.Equals(telephony.Value!.RetellAgentId, agentId, StringComparison.Ordinal))
            return Result<RetellCallRecorded>.Failure("The call's agent is not this tenant's agent.", RetellCallFailureCodes.AgentMismatch);

        var analysis = call.TryGetProperty("call_analysis", out var a) && a.ValueKind == JsonValueKind.Object ? a : (JsonElement?)null;
        var command = new UpsertVoiceCallCommand(
            RetellCallId: callId,
            TwilioCallSid: Text(call, "telephony_identifier", "twilio_call_sid"),
            CallerNumber: caller,
            CallStatus: status,
            StartedAt: DateTimeOffset.FromUnixTimeMilliseconds(started.Value),
            EndedAt: ended is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(ended.Value),
            DurationSeconds: (int)((duration ?? 0) / 1000),
            BillableMinutes: BillableMinutes(duration ?? 0),
            Transcript: Transcript(call),
            CallSummary: analysis is null ? null : Text(analysis.Value, "call_summary"),
            Disposition: null,
            RecordingUrl: Text(call, "recording_url"));

        var stored = await _store.UpsertCallAsync(tenantId, command, ct).ConfigureAwait(false);
        if (!stored.IsSuccess) return Result<RetellCallRecorded>.Failure(stored.Error!, stored.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        _logger.LogInformation("Tenant {TenantId}: Retell {Event} recorded as call {CallId}, {Minutes} billable minutes.",
            tenantId, evt, stored.Value!.CallId, command.BillableMinutes);
        return Result<RetellCallRecorded>.Success(new RetellCallRecorded(evt, stored.Value.CallId));
    }

    /// <summary>
    /// Who said what, in order: each utterance's role and words. The per-word timings Retell adds are left out. A call in which nobody
    /// spoke has an empty transcript.
    /// </summary>
    private static JsonElement Transcript(JsonElement call)
    {
        var utterances = new List<object>();
        if (call.TryGetProperty("transcript_object", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var role = Text(item, "role");
                var content = Text(item, "content");
                if (role is not null && content is not null) utterances.Add(new { role, content });
            }
        }

        return JsonSerializer.SerializeToElement(utterances);
    }

    private static Result<RetellCallRecorded> Invalid(string message) => Result<RetellCallRecorded>.Failure(message, RetellCallFailureCodes.InvalidCall);

    private static string? Text(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }

        return current.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(current.GetString()) ? current.GetString() : null;
    }

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= 0 ? n : null;
}
