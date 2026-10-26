using System.Globalization;
using System.Text;
using GeekApplication.Models.Visitors;

namespace GeekAPI.Services.Visitors;

public sealed record VisitorDailyReportContent(
    string DateKey,
    string Title,
    string Body,
    VisitorReportTotals Totals,
    byte[] SessionsCsv,
    byte[] PagesCsv,
    byte[] InteractionsCsv);

/// <summary>
/// One New York day as text: the totals, the top-ten lists, and one block per session with the
/// highest score first. The body stops at 100 sessions; the CSVs hold every one.
/// </summary>
public static class VisitorDailyReportBuilder
{
    public const int BodySessionCap = 100;
    public const int TopListSize = 10;
    public static readonly TimeSpan VisitorScoreWindow = TimeSpan.FromDays(30);

    public static VisitorDailyReportContent Build(
        DateOnly day,
        IReadOnlyList<VisitorSession> sessions,
        IReadOnlyList<VisitorProfile> profiles,
        VisitorFilterDay? drops,
        IReadOnlyDictionary<Guid, double> visitorScores)
    {
        var profileById = profiles.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var ordered = sessions.OrderByDescending(s => s.Score).ThenBy(s => s.StartedAt).ToList();
        var totals = Totals(ordered, profileById, drops);
        var dateKey = VisitorClock.DayKey(day);

        var title = $"Website visitors, {VisitorClock.LongDay(day)}: "
            + $"{totals.Visitors} {Plural(totals.Visitors, "visitor")}, "
            + $"{totals.Sessions} {Plural(totals.Sessions, "session")}, "
            + $"{totals.IdentifiedVisitors} identified";

        var sb = new StringBuilder();
        sb.Append("Website visitors for ").Append(VisitorClock.LongDay(day)).Append(" (America/New_York)\n\n");

        sb.Append("TOTALS\n");
        sb.Append("Visitors: ").Append(totals.Visitors)
          .Append(" (").Append(totals.NewVisitors).Append(" new, ").Append(totals.ReturningVisitors).Append(" returning)\n");
        sb.Append("Sessions: ").Append(totals.Sessions).Append('\n');
        sb.Append("Page views: ").Append(totals.PageViews).Append('\n');
        sb.Append("Median active time per session: ").Append(totals.MedianActiveSeconds).Append(" s\n");
        sb.Append("Florida sessions: ").Append(totals.FloridaSessions).Append('\n');
        sb.Append("Identified visitors: ").Append(totals.IdentifiedVisitors).Append('\n');
        sb.Append("Intent sessions (Florida, score 60 or more): ").Append(totals.IntentSessions).Append('\n');
        sb.Append("Dropped requests: ").Append(totals.Drops.Count == 0
            ? "none"
            : string.Join(", ", totals.Drops.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value}"))).Append("\n\n");

        if (NothingArrived(totals))
        {
            sb.Append("NOTHING ARRIVED\n");
            sb.Append("No sessions and no dropped requests were recorded for this day. On a public site that means the site did not pass its visits on, not that nobody came. Check GEEK_API_URL and GEEK_BACKEND_API_KEY on Vercel, and that GeekAPI was reachable from the site.\n\n");
        }

        TopList(sb, "TOP LANDING PAGES", ordered.Select(s => LandingPath(s)));
        TopList(sb, "TOP REFERRERS", ordered.Select(s => VisitorReportText.ReferrerHost(s.Arrival?.Referrer) ?? "direct"));
        TopList(sb, "TOP UTM SOURCES", ordered.Select(s => s.Arrival?.Utm?.Source).Where(v => !string.IsNullOrWhiteSpace(v))!);
        TopList(sb, "TOP UTM CAMPAIGNS", ordered.Select(s => s.Arrival?.Utm?.Campaign).Where(v => !string.IsNullOrWhiteSpace(v))!);
        TopList(sb, "TOP CITIES", ordered.Select(s => VisitorReportText.Place(s) ?? "unknown"));
        TopList(sb, "TOP DEVICE TYPES", ordered.Select(s => s.UserAgent?.DeviceType ?? "unknown"));
        TopList(sb, "TOP BROWSERS", ordered.Select(s => s.UserAgent?.Browser ?? "unknown"));

        sb.Append("SESSIONS (highest score first; ").Append(ordered.Count).Append(" in all, ")
          .Append(Math.Min(ordered.Count, BodySessionCap)).Append(" shown; every session is in sessions-")
          .Append(dateKey).Append(".csv)\n\n");
        if (ordered.Count == 0) sb.Append("No sessions were recorded.\n");

        var n = 0;
        foreach (var s in ordered.Take(BodySessionCap))
        {
            n++;
            profileById.TryGetValue(s.VisitorId, out var profile);
            visitorScores.TryGetValue(s.VisitorId, out var score30);
            SessionBlock(sb, n, s, profile, score30);
        }

        var scoresForCsv = visitorScores;
        return new VisitorDailyReportContent(
            dateKey,
            title,
            sb.ToString(),
            totals,
            VisitorReportCsv.Sessions(ordered, profileById, scoresForCsv),
            VisitorReportCsv.Pages(ordered),
            VisitorReportCsv.Interactions(ordered));
    }

    /// <summary>Each visitor's session scores summed over the window, computed on read.</summary>
    public static IReadOnlyDictionary<Guid, double> VisitorScores(IEnumerable<VisitorSession> recentSessions) =>
        recentSessions.GroupBy(s => s.VisitorId).ToDictionary(g => g.Key, g => Math.Round(g.Sum(s => s.Score), 1));

    public static VisitorReportTotals Totals(
        IReadOnlyList<VisitorSession> sessions,
        IReadOnlyDictionary<Guid, VisitorProfile> profiles,
        VisitorFilterDay? drops)
    {
        var visitorIds = sessions.Select(s => s.VisitorId).Distinct().ToList();
        var newVisitors = sessions.Where(s => s.VisitNumber <= 1).Select(s => s.VisitorId).Distinct().Count();
        var activeSeconds = sessions.Select(VisitorReportCsv.ActiveSeconds).OrderBy(v => v).ToList();
        return new VisitorReportTotals
        {
            Visitors = visitorIds.Count,
            NewVisitors = newVisitors,
            ReturningVisitors = visitorIds.Count - newVisitors,
            Sessions = sessions.Count,
            PageViews = sessions.Sum(s => s.Pages?.Count ?? 0),
            MedianActiveSeconds = (int)Median(activeSeconds),
            FloridaSessions = sessions.Count(s => s.Florida),
            IdentifiedVisitors = visitorIds.Count(id => profiles.TryGetValue(id, out var p) && p.Identity is not null),
            IntentSessions = sessions.Count(s => s.IntentReachedAt is not null),
            Drops = drops?.Counts is { } counts ? new Dictionary<string, int>(counts) : new Dictionary<string, int>(),
        };
    }

    internal static double Median(IReadOnlyList<long> sorted)
    {
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
    }

    private static void TopList(StringBuilder sb, string title, IEnumerable<string> values)
    {
        var top = values
            .GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => (Value: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Value, StringComparer.Ordinal)
            .Take(TopListSize)
            .ToList();
        sb.Append(title).Append('\n');
        if (top.Count == 0) sb.Append("  none\n");
        foreach (var (value, count) in top)
            sb.Append("  ").Append(count.ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append("  ").Append(value).Append('\n');
        sb.Append('\n');
    }

    private static void SessionBlock(StringBuilder sb, int n, VisitorSession s, VisitorProfile? profile, double score30)
    {
        sb.Append(n).Append(". Score ").Append(s.Score.ToString("0.#", CultureInfo.InvariantCulture));
        if (s.Florida) sb.Append("  Florida");
        sb.Append("  ").Append(VisitorReportText.Place(s) ?? "location unknown");
        if (profile?.Identity is { } id)
            sb.Append("  ").Append(id.FirstName).Append(' ').Append(id.LastName).Append(" (").Append(id.Email).Append(", ").Append(id.Phone).Append(')');
        sb.Append('\n');

        sb.Append("   Started ").Append(VisitorClock.InNewYork(s.StartedAt));
        var zone = s.Geo?.Timezone?.Trim();
        if (!string.IsNullOrEmpty(zone) && !string.Equals(zone, VisitorClock.NewYorkZoneId, StringComparison.Ordinal))
        {
            var local = VisitorClock.InVisitorZone(s.StartedAt, zone);
            if (!local.EndsWith(" ET", StringComparison.Ordinal)) sb.Append(", visitor's local time ").Append(local);
        }
        sb.Append(", active ").Append(VisitorReportCsv.ActiveSeconds(s)).Append(" s\n");

        sb.Append("   IP ").Append(s.Ip);
        if (s.AdditionalIps is { Count: > 0 } more) sb.Append(" (also ").Append(string.Join(", ", more)).Append(')');
        if (!string.IsNullOrWhiteSpace(s.Geo?.PostalCode)) sb.Append(", postal code ").Append(s.Geo.PostalCode);
        if (s.Geo?.Latitude is { } lat && s.Geo?.Longitude is { } lng)
            sb.Append(", ").Append(lat.ToString("0.###", CultureInfo.InvariantCulture)).Append(", ").Append(lng.ToString("0.###", CultureInfo.InvariantCulture));
        if (s.BrowserGeo is { } pos)
        {
            sb.Append("; browser position ")
              .Append(pos.Latitude.ToString("0.####", CultureInfo.InvariantCulture)).Append(", ")
              .Append(pos.Longitude.ToString("0.####", CultureInfo.InvariantCulture));
            if (pos.AccuracyMeters is { } acc) sb.Append(" within ").Append(acc.ToString("0", CultureInfo.InvariantCulture)).Append(" m");
        }
        if (!string.IsNullOrWhiteSpace(s.EdgeRegion)) sb.Append("; edge ").Append(s.EdgeRegion);
        sb.Append('\n');

        sb.Append("   Device: ").Append(VisitorReportText.Device(s));
        var screen = s.Environment?.Screen;
        if (screen?.Width is { } w && screen.Height is { } h)
        {
            sb.Append("; screen ").Append(w).Append('x').Append(h);
            if (screen.ViewportWidth is { } vw && screen.ViewportHeight is { } vh) sb.Append(", viewport ").Append(vw).Append('x').Append(vh);
        }
        var language = s.Environment?.Browser?.Languages is { Count: > 0 } langs ? langs[0] : FirstLanguage(s.AcceptLanguage);
        if (!string.IsNullOrWhiteSpace(language)) sb.Append("; language ").Append(language);
        if (s.Dnt == "1") sb.Append("; DNT on");
        sb.Append('\n');

        sb.Append("   Referrer: ").Append(VisitorReportText.ReferrerHost(s.Arrival?.Referrer) ?? "direct");
        var utm = VisitorReportText.Utm(s.Arrival?.Utm);
        if (utm is not null) sb.Append("; ").Append(utm);
        var clicks = ClickIds(s.Arrival?.ClickIds);
        if (clicks is not null) sb.Append("; ").Append(clicks);
        sb.Append("; landing ").Append(s.Arrival?.LandingUrl ?? LandingPath(s)).Append('\n');

        sb.Append("   Pages:\n");
        foreach (var page in s.Pages ?? new List<VisitorPageView>())
        {
            sb.Append("     ").Append(page.Path);
            if (!string.IsNullOrEmpty(page.Query)) sb.Append(page.Query);
            sb.Append("  ").Append((page.ActiveMs / 1000).ToString(CultureInfo.InvariantCulture)).Append(" s");
            if (page.MaxScrollPct > 0) sb.Append(", scrolled ").Append(page.MaxScrollPct).Append('%');
            if (page.Clicks is { Count: > 0 } pc)
            {
                sb.Append(", ").Append(pc.Count).Append(pc.Count == 1 ? " click: " : " clicks: ");
                sb.Append(string.Join(", ", pc.Take(5).Select(c => c.LinkId ?? c.Href ?? "link")));
                if (pc.Count > 5) sb.Append(", and ").Append(pc.Count - 5).Append(" more");
            }
            sb.Append('\n');
        }

        var funnel = new List<string>();
        if (s.Scheduler?.ViewedAt is not null) funnel.Add("viewed");
        if (s.Scheduler?.DatePickedAt is not null) funnel.Add("date picked");
        if (s.Scheduler?.SlotHeldAt is not null) funnel.Add("slot held");
        if (s.Scheduler?.BookedAt is not null) funnel.Add("booked");
        sb.Append("   Scheduler: ").Append(funnel.Count == 0 ? "not viewed" : string.Join(", ", funnel));
        if (s.ContactSubmittedAt is not null) sb.Append("; contact form submitted");
        if (s.IntentReachedAt is not null) sb.Append("; intent reached");
        sb.Append('\n');

        sb.Append("   Visit ").Append(s.VisitNumber).Append(" of this visitor");
        if (profile is not null) sb.Append(" (").Append(profile.SessionCount).Append(" in all since ").Append(VisitorClock.InNewYork(profile.FirstSeenAt)).Append(')');
        sb.Append("; 30-day score ").Append(score30.ToString("0.#", CultureInfo.InvariantCulture)).Append("\n\n");
    }

    private static string? ClickIds(VisitorClickIds? ids)
    {
        if (ids is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ids.Gclid)) parts.Add("gclid");
        if (!string.IsNullOrWhiteSpace(ids.Gbraid)) parts.Add("gbraid");
        if (!string.IsNullOrWhiteSpace(ids.Wbraid)) parts.Add("wbraid");
        if (!string.IsNullOrWhiteSpace(ids.Fbclid)) parts.Add("fbclid");
        if (!string.IsNullOrWhiteSpace(ids.Msclkid)) parts.Add("msclkid");
        if (!string.IsNullOrWhiteSpace(ids.LiFatId)) parts.Add("li_fat_id");
        if (!string.IsNullOrWhiteSpace(ids.Ttclid)) parts.Add("ttclid");
        return parts.Count == 0 ? null : "click ids " + string.Join(", ", parts);
    }

    private static string? FirstLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;
        var first = acceptLanguage.Split(',')[0].Split(';')[0].Trim();
        return first.Length == 0 ? null : first;
    }

    internal static string LandingPath(VisitorSession s)
    {
        if (s.Pages is { Count: > 0 } pages) return pages[0].Path;
        if (Uri.TryCreate(s.Arrival?.LandingUrl, UriKind.Absolute, out var uri)) return uri.AbsolutePath;
        return "/";
    }

    /// <summary>
    /// A day with no sessions and no dropped requests. Bots and scanners reach any public hostname
    /// every day and are counted as drops, so a day with neither is a day on which the site passed
    /// nothing on, whatever the reason.
    /// </summary>
    internal static bool NothingArrived(VisitorReportTotals totals) =>
        totals.Sessions == 0 && totals.Drops.Values.Sum() == 0;

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";
}
