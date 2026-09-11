using System.Text;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Normalized edit distance for ROI quality telemetry (0 = identical, 1 = fully rewritten).
/// Not a cash metric — workflow rewrite effort only.
/// </summary>
internal static class GccV2EditDistanceMetrics
{
    public const int MaxComparedChars = 1200;

    public sealed record Aggregate(
        int Samples,
        double? MeanEditDistance,
        string Message);

    /// <summary>
    /// Returns normalized Levenshtein distance in [0, 1]. Null when either side is empty.
    /// </summary>
    public static double? NormalizedDistance(string? before, string? after)
    {
        var left = Normalize(before);
        var right = Normalize(after);
        if (left.Length == 0 || right.Length == 0) return null;
        if (string.Equals(left, right, StringComparison.Ordinal)) return 0d;

        left = Truncate(left, MaxComparedChars);
        right = Truncate(right, MaxComparedChars);
        var distance = Levenshtein(left, right);
        var denom = Math.Max(left.Length, right.Length);
        return denom == 0 ? null : Math.Round(distance / (double)denom, 4);
    }

    public static Aggregate Combine(IEnumerable<double> distances)
    {
        var list = distances.Where(d => d is >= 0 and <= 1).ToList();
        if (list.Count == 0)
        {
            return new Aggregate(
                0,
                null,
                "No write→edit pairs with comparable text in the lookback window.");
        }

        var mean = Math.Round(list.Average(), 4);
        return new Aggregate(
            list.Count,
            mean,
            "meanEditDistance is normalized rewrite effort (0 identical → 1 fully rewritten), not cash ROI.");
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }
}
