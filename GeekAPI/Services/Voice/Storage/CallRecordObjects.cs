using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// Builds the object a finished call is written as, at the path the plan sets out:
/// <c>YYYY/MM/DD/call-{timestamp}-{last4}.{md|json}</c> (plans/client-and-app-plan.md, "Call logs and transcripts (BYOS)").
///
/// A business that asked for its transcripts gets JSON, which keeps every turn and who said it without a parser having to guess. A
/// business that asked only for summaries gets the Markdown a person reads, which is what that half of the plan's extension is for.
/// Nothing here is a corpus and nothing re-reads it: it is written once into storage the business owns, and this platform keeps no
/// copy and has no way to read it back.
/// </summary>
public static class CallRecordObjects
{
    public const string JsonContentType = "application/json";
    public const string MarkdownContentType = "text/markdown";

    /// <summary>The last four digits of a caller's number, or <c>unknown</c> when Twilio gave none.</summary>
    public static string Last4(string? callerNumber)
    {
        if (string.IsNullOrWhiteSpace(callerNumber)) return "unknown";
        var digits = new string(callerNumber.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : "unknown";
    }

    /// <summary>The plan's path for a call that ended at <paramref name="endedAt"/>, in UTC so a day's folder means one thing.</summary>
    public static string PathFor(DateTimeOffset endedAt, string? callerNumber, string extension)
    {
        var utc = endedAt.ToUniversalTime();
        return $"{utc:yyyy}/{utc:MM}/{utc:dd}/call-{utc.ToUnixTimeSeconds()}-{Last4(callerNumber)}.{extension}";
    }

    /// <summary>The whole record as JSON: the outcome, what the caller gave, what was done, and the turns when the business asked for them.</summary>
    public static CallRecordObject Json(FinishedCall call)
    {
        var payload = new
        {
            endedAt = call.EndedAt.ToUniversalTime(),
            durationSeconds = (int)call.Duration.TotalSeconds,
            businessName = call.BusinessName,
            callerNumber = call.CallerNumber,
            outcome = call.Summary?.Outcome,
            summary = call.Summary?.Summary,
            summaryAvailable = call.Kind == FinishedCallKind.Summarized,
            fields = call.Summary?.Fields.Select(f => new { label = f.Label, value = f.Value }),
            actions = call.Summary?.Actions,
            unanswered = call.Summary?.Unanswered,
            transcript = call.Transcript?.Select(t => new { speaker = t.Speaker.ToString(), text = t.Text, tool = t.ToolName }),
        };

        return new CallRecordObject(
            PathFor(call.EndedAt, call.CallerNumber, "json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            JsonContentType);
    }

    /// <summary>The record as the short report a person reads, for a business that asked for summaries and not transcripts.</summary>
    public static CallRecordObject Markdown(FinishedCall call)
    {
        var body = new StringBuilder();
        body.Append("# Call — ").Append(call.EndedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")).Append("\n\n");
        body.Append("- **Outcome:** ").Append(call.Summary?.Outcome ?? "Summary unavailable").Append('\n');
        body.Append("- **Caller:** ").Append(string.IsNullOrWhiteSpace(call.CallerNumber) ? "Not provided" : call.CallerNumber).Append('\n');
        body.Append("- **Duration:** ").Append((int)call.Duration.TotalSeconds).Append(" seconds\n\n");

        if (!string.IsNullOrWhiteSpace(call.Summary?.Summary))
            body.Append(call.Summary.Summary).Append("\n\n");

        Section(body, "What the caller gave", call.Summary?.Fields.Select(f => $"**{f.Label}:** {f.Value}"));
        Section(body, "What the receptionist did", call.Summary?.Actions);
        Section(body, "Left unanswered", call.Summary?.Unanswered);

        return new CallRecordObject(PathFor(call.EndedAt, call.CallerNumber, "md"), body.ToString(), MarkdownContentType);
    }

    private static void Section(StringBuilder body, string heading, IEnumerable<string>? lines)
    {
        var items = lines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (items is null || items.Count == 0) return;

        body.Append("## ").Append(heading).Append("\n\n");
        foreach (var item in items) body.Append("- ").Append(item).Append('\n');
        body.Append('\n');
    }
}
