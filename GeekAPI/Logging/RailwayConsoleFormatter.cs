using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace GeekAPI.Logging;

/// <summary>
/// One JSON line per log entry, in the shape Railway indexes: <c>level</c> and <c>message</c> at the top,
/// lower-case, with the rendered text in <c>message</c>.
/// </summary>
/// <remarks>
/// <para>
/// The stock JSON console writer puts the rendered text under <c>Message</c> inside the entry's
/// attributes and leaves the top-level message empty. Railway's log search reads the top-level
/// message, so on 2026-10-06 a search for "Grounding resolved" found nothing while the line was
/// there, and every diagnosis meant pulling whole windows of HTTP chatter and reading them by eye.
/// </para>
/// <para>
/// The structured state is kept, under <c>state</c>, so nothing the old shape carried is lost; the
/// category, event id and exception are top-level too. Scopes are not written.
/// </para>
/// </remarks>
public sealed class RailwayConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "railway";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? logEntry.State?.ToString() ?? string.Empty;
        if (message.Length == 0 && logEntry.Exception is null) return;

        var entry = new Dictionary<string, object?>
        {
            ["time"] = DateTime.UtcNow.ToString("O"),
            ["level"] = Level(logEntry.LogLevel),
            ["message"] = message,
            ["category"] = logEntry.Category,
        };
        if (logEntry.EventId.Id != 0) entry["eventId"] = logEntry.EventId.Id;
        if (logEntry.Exception is not null) entry["exception"] = logEntry.Exception.ToString();

        if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> state && state.Count > 0)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in state)
            {
                if (key == "{OriginalFormat}") continue;
                fields[key] = value switch
                {
                    null => null,
                    string or bool or int or long or double or decimal or Guid or DateTime or DateTimeOffset => value,
                    _ => value.ToString(),
                };
            }

            if (fields.Count > 0) entry["state"] = fields;
        }

        textWriter.WriteLine(JsonSerializer.Serialize(entry, Json));
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "fatal",
        _ => "info",
    };
}
