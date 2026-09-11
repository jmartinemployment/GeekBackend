namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Parse pasted newline/CSV topic lists into unique grid row topics.</summary>
public static class GccV2GridTopicImport
{
    public const int MaxTopicsPerImport = 100;
    public const int MaxTotalRows = 500;

    public static IReadOnlyList<string> Parse(string? text, IReadOnlyList<string>? topics = null)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (topics is not null)
        {
            foreach (var topic in topics)
                TryAdd(topic, result, seen);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
                TryAdd(FirstCsvField(line), result, seen);
        }

        return result;
    }

    private static void TryAdd(string? raw, List<string> result, HashSet<string> seen)
    {
        var topic = Normalize(raw);
        if (topic is null) return;
        if (!seen.Add(topic)) return;
        result.Add(topic);
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var topic = raw.Trim().TrimStart('\uFEFF');
        return topic.Length == 0 ? null : topic;
    }

    private static string FirstCsvField(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (trimmed[0] == '"')
        {
            var end = 1;
            while (end < trimmed.Length)
            {
                if (trimmed[end] == '"' && end + 1 < trimmed.Length && trimmed[end + 1] == '"')
                {
                    end += 2;
                    continue;
                }
                if (trimmed[end] == '"')
                    return trimmed[1..end].Replace("\"\"", "\"", StringComparison.Ordinal);
                end++;
            }
            return trimmed[1..].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        var comma = trimmed.IndexOf(',');
        return comma < 0 ? trimmed : trimmed[..comma].Trim();
    }
}
