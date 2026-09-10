using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Daily/weekly/monthly grid schedule stored inside grid ConfigJson.</summary>
public static class GccV2GridSchedule
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public record State(
        string Cadence, bool Enabled, string Mode, int SampleSize, string? NextRunAt, string? LastRunAt);

    public static State Read(string? configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (!doc.RootElement.TryGetProperty("schedule", out var schedule)
                || schedule.ValueKind != JsonValueKind.Object)
                return new State("none", false, "sample", 10, null, null);

            var cadence = schedule.TryGetProperty("cadence", out var cadenceEl)
                ? (cadenceEl.GetString() ?? "none").Trim().ToLowerInvariant()
                : "none";
            if (cadence is not ("daily" or "weekly" or "monthly"))
                cadence = "none";
            var mode = schedule.TryGetProperty("mode", out var modeEl)
                && string.Equals(modeEl.GetString(), "full", StringComparison.OrdinalIgnoreCase)
                ? "full"
                : "sample";
            var sampleSize = schedule.TryGetProperty("sampleSize", out var sizeEl)
                && sizeEl.TryGetInt32(out var size)
                && size > 0
                ? size
                : 10;
            var enabled = schedule.TryGetProperty("enabled", out var enabledEl)
                && enabledEl.ValueKind == JsonValueKind.True
                && cadence != "none";
            var nextRunAt = schedule.TryGetProperty("nextRunAt", out var nextEl)
                ? nextEl.GetString()
                : null;
            var lastRunAt = schedule.TryGetProperty("lastRunAt", out var lastEl)
                ? lastEl.GetString()
                : null;
            return new State(cadence, enabled, mode, sampleSize, nextRunAt, lastRunAt);
        }
        catch (JsonException)
        {
            return new State("none", false, "sample", 10, null, null);
        }
    }

    public static string Merge(string? configJson, State schedule)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                root[prop.Name] = prop.Value.Clone();
        }

        root["schedule"] = new
        {
            cadence = schedule.Cadence,
            enabled = schedule.Enabled,
            mode = schedule.Mode,
            sampleSize = schedule.SampleSize,
            nextRunAt = schedule.NextRunAt,
            lastRunAt = schedule.LastRunAt,
        };
        return JsonSerializer.Serialize(root, JsonOpts);
    }

    public static DateTimeOffset? AdvanceNextRunAt(DateTimeOffset from, string cadence) =>
        cadence switch
        {
            "daily" => from.AddDays(1),
            "weekly" => from.AddDays(7),
            "monthly" => from.AddMonths(1),
            _ => null,
        };

    public static bool IsDue(State schedule, DateTimeOffset now)
    {
        if (!schedule.Enabled || schedule.Cadence == "none" || string.IsNullOrWhiteSpace(schedule.NextRunAt))
            return false;
        return DateTimeOffset.TryParse(schedule.NextRunAt, out var dueAt) && dueAt <= now;
    }
}
