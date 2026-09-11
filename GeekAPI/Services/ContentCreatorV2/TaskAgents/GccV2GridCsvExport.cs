using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>Serialize grid rows to CSV for portfolio export / re-import.</summary>
public static class GccV2GridCsvExport
{
    public static string Build(GccV2GridDto grid)
    {
        var sb = new StringBuilder();
        sb.AppendLine("topic,status,rowIndex,result,error,updatedAt");
        foreach (var row in grid.Rows.OrderBy(r => r.RowIndex))
        {
            var topic = ReadTopic(row.InputJson);
            var result = ReadResult(row.OutputJson);
            sb.Append(Escape(topic)).Append(',')
                .Append(Escape(row.Status ?? "")).Append(',')
                .Append(row.RowIndex).Append(',')
                .Append(Escape(result)).Append(',')
                .Append(Escape(row.Error ?? "")).Append(',')
                .Append(Escape(row.UpdatedAtUtc.ToString("O")))
                .AppendLine();
        }
        return sb.ToString();
    }

    public static string FileName(string? gridName)
    {
        var raw = string.IsNullOrWhiteSpace(gridName) ? "grid" : gridName.Trim();
        var sanitized = new string(raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        while (sanitized.Contains("--", StringComparison.Ordinal))
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        sanitized = sanitized.Trim('-');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "grid";
        return $"{sanitized}.csv";
    }

    private static string ReadTopic(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return "";
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("topic", out var topic)
                && topic.ValueKind == JsonValueKind.String)
            {
                return topic.GetString()?.Trim() ?? "";
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return "";
    }

    private static string ReadResult(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return "";
        try
        {
            using var document = JsonDocument.Parse(outputJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.String)
            {
                return result.GetString()?.Trim() ?? "";
            }
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(['"', ',', '\r', '\n']) < 0) return value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
