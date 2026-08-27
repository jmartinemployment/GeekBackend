using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Diagnostics;

/// <summary>Debug-mode NDJSON logger for session fa72fe. Do not log secrets.</summary>
internal static class AgentDebugLog
{
    private const string SessionId = "fa72fe";
    private const string LogPath = "/Users/jeffmartin/development/content-creator-v2/.cursor/debug-fa72fe.log";
    private const string IngestUrl = "http://127.0.0.1:7816/ingest/22ee2238-7bb8-4fc3-9705-0dea2c361cf3";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(750) };

    public static void Write(string hypothesisId, string location, string message, object? data = null)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            var line = JsonSerializer.Serialize(payload);
            // Railway / prod: console is the only sink we can pull via get-logs.
            try { Console.WriteLine("[agent-debug-fa72fe] " + line); } catch { /* ignore */ }
            try { File.AppendAllText(LogPath, line + "\n"); } catch { /* ignore */ }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, IngestUrl);
                req.Content = new StringContent(line, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("X-Debug-Session-Id", SessionId);
                _ = Http.SendAsync(req);
            }
            catch { /* ignore */ }
        }
        catch
        {
            // never break generate for debug logging
        }
    }
}
