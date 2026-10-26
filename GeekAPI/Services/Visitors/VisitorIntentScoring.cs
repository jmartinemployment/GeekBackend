using GeekApplication.Models.Visitors;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// The intent score of a session, from its pages, their active time and the scheduler. One file
/// of constants, so the weights can be read in one place and changed in one place.
/// </summary>
public static class VisitorIntentScoring
{
    public const double IntentThreshold = 60;
    public const double MaxScore = 100;
    public const int ActiveSecondsCap = 120;
    public const double DistinctPageBonus = 5;
    public const double DistinctPageBonusCap = 20;
    public const double SchedulerViewBonus = 25;

    public const double FloridaLatitudeMin = 24.3;
    public const double FloridaLatitudeMax = 31.1;
    public const double FloridaLongitudeMin = -87.7;
    public const double FloridaLongitudeMax = -79.8;

    /// <summary>Segment rules match the path itself and anything under it; prefix rules match any path starting with them.</summary>
    public static double PathWeight(string? path)
    {
        var p = Normalize(path);
        if (p == "/") return 2;
        if (Segment(p, "/proposals")) return 50;
        if (p.StartsWith("/pricing", StringComparison.Ordinal)) return 40;
        if (Segment(p, "/architectural-infrastructure-audit")) return 30;
        if (Segment(p, "/use-cases")) return 10;
        if (Segment(p, "/tools")) return 8;
        if (Segment(p, "/methodology")) return 8;
        if (Segment(p, "/blog")) return 3;
        if (Segment(p, "/glossary")) return 2;
        return 0;
    }

    /// <summary>
    /// Per distinct path: weight × (0.25 + 0.75 × min(activeSeconds, 120) / 120), the seconds of
    /// repeat views summed before the cap; +5 for each distinct path after the first, to +20; +25
    /// once when the scheduler was viewed; capped at 100. Never lower than the score stored, which
    /// the store enforces by keeping the greater of the two.
    /// </summary>
    public static double Score(VisitorSession session)
    {
        var activeByPath = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var page in session.Pages ?? new List<VisitorPageView>())
        {
            var key = Normalize(page.Path);
            activeByPath[key] = activeByPath.TryGetValue(key, out var ms) ? ms + Math.Max(0, page.ActiveMs) : Math.Max(0, page.ActiveMs);
        }

        double total = 0;
        foreach (var (path, activeMs) in activeByPath)
        {
            var seconds = Math.Min(activeMs / 1000d, ActiveSecondsCap);
            total += PathWeight(path) * (0.25 + 0.75 * seconds / ActiveSecondsCap);
        }

        if (activeByPath.Count > 1)
            total += Math.Min(DistinctPageBonus * (activeByPath.Count - 1), DistinctPageBonusCap);

        if (session.Scheduler?.ViewedAt is not null)
            total += SchedulerViewBonus;

        return Math.Round(Math.Min(total, MaxScore), 1, MidpointRounding.AwayFromZero);
    }

    public static bool ReachesIntent(double score) => score >= IntentThreshold;

    public static bool IsInFlorida(double latitude, double longitude) =>
        latitude >= FloridaLatitudeMin && latitude <= FloridaLatitudeMax
        && longitude >= FloridaLongitudeMin && longitude <= FloridaLongitudeMax;

    internal static string Normalize(string? path)
    {
        var p = (path ?? "/").Trim();
        var q = p.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) p = p[..q];
        if (p.Length == 0 || p[0] != '/') p = "/" + p;
        while (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return p.ToLowerInvariant();
    }

    private static bool Segment(string path, string segment) =>
        path == segment || path.StartsWith(segment + "/", StringComparison.Ordinal);
}
