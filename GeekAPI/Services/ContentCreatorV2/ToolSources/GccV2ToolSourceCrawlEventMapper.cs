using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

internal static class GccV2ToolSourceCrawlEventMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static object MapRun(GccV2ToolSourceCrawlRunDto run, string? currentOrigin = null) =>
        new
        {
            runId = run.Id,
            status = run.Status,
            seedUrls = TryParseJsonArray(run.SeedUrlsJson),
            hosts = TryParseJsonArray(run.HostProgressJson),
            errorSummary = run.ErrorSummary,
            startedAtUtc = run.StartedAtUtc,
            completedAtUtc = run.CompletedAtUtc,
            currentOrigin,
        };

    private static object? TryParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
