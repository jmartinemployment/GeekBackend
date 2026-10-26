using System.Globalization;
using System.Text;
using GeekApplication.Models.Visitors;

namespace GeekAPI.Services.Visitors;

/// <summary>
/// The three CSV files. RFC 4180 quoting, CRLF rows, UTF-8 with a byte order mark so Excel
/// opens them as UTF-8. The column lists are public so a test can hold them still.
/// </summary>
public static class VisitorReportCsv
{
    public static readonly IReadOnlyList<string> SessionColumns = new[]
    {
        "sessionId", "visitorId", "visitNumber", "startedAt", "lastSeenAt", "score", "florida",
        "ip", "additionalIps", "country", "region", "city", "postalCode", "latitude", "longitude", "timezone", "continent",
        "browserLatitude", "browserLongitude", "browserAccuracyMeters", "edgeRegion",
        "browser", "browserVersion", "os", "osVersion", "deviceType", "brand", "model", "userAgent",
        "chUa", "chMobile", "chPlatform", "acceptLanguage", "dnt",
        "landingUrl", "referrer", "utmSource", "utmMedium", "utmCampaign", "utmTerm", "utmContent",
        "gclid", "gbraid", "wbraid", "fbclid", "msclkid", "liFatId", "ttclid",
        "screenWidth", "screenHeight", "availWidth", "availHeight", "viewportWidth", "viewportHeight", "devicePixelRatio", "colorDepth", "orientation",
        "platform", "hardwareConcurrency", "deviceMemory", "maxTouchPoints", "pointer", "hover",
        "languages", "browserTimezone", "utcOffsetMinutes", "cookieEnabled", "pdfViewerEnabled", "displayMode", "colorScheme", "reducedMotion",
        "platformVersion", "hintModel", "architecture", "bitness", "fullVersionList",
        "effectiveType", "downlink", "rtt", "saveData",
        "pageViews", "activeSeconds", "schedulerViewedAt", "datePickedAt", "slotHeldAt", "bookedAt", "contactSubmittedAt",
        "intentReachedAt",
        "identityFirstName", "identityLastName", "identityEmail", "identityPhone", "identitySource", "identifiedAt",
        "visitorFirstSeenAt", "visitorSessionCount", "visitorScore30d",
    };

    public static readonly IReadOnlyList<string> PageColumns = new[]
    {
        "sessionId", "visitorId", "pageViewId", "path", "query", "title", "enteredAt", "activeMs", "maxScrollPct", "clicks",
        "lcp", "inp", "cls", "fcp", "ttfb",
    };

    public static readonly IReadOnlyList<string> InteractionColumns = new[]
    {
        "sessionId", "visitorId", "kind", "at", "path", "href", "linkId", "outbound",
    };

    public static byte[] Sessions(
        IEnumerable<VisitorSession> sessions,
        IReadOnlyDictionary<Guid, VisitorProfile> profiles,
        IReadOnlyDictionary<Guid, double> visitorScores)
    {
        var sb = new StringBuilder();
        Row(sb, SessionColumns);
        foreach (var s in sessions)
        {
            profiles.TryGetValue(s.VisitorId, out var profile);
            visitorScores.TryGetValue(s.VisitorId, out var score30);
            var env = s.Environment;
            var arrival = s.Arrival;
            Row(sb, new[]
            {
                s.Id.ToString(), s.VisitorId.ToString(), Num(s.VisitNumber), Date(s.StartedAt), Date(s.LastSeenAt), Num(s.Score), Bool(s.Florida),
                s.Ip, string.Join(' ', s.AdditionalIps ?? new List<string>()),
                s.Geo?.Country, s.Geo?.Region, s.Geo?.City, s.Geo?.PostalCode, Num(s.Geo?.Latitude), Num(s.Geo?.Longitude), s.Geo?.Timezone, s.Geo?.Continent,
                Num(s.BrowserGeo?.Latitude), Num(s.BrowserGeo?.Longitude), Num(s.BrowserGeo?.AccuracyMeters), s.EdgeRegion,
                s.UserAgent?.Browser, s.UserAgent?.BrowserVersion, s.UserAgent?.Os, s.UserAgent?.OsVersion, s.UserAgent?.DeviceType, s.UserAgent?.Brand, s.UserAgent?.Model, s.UserAgent?.Raw,
                s.ClientHints?.Ua, s.ClientHints?.Mobile, s.ClientHints?.Platform, s.AcceptLanguage, s.Dnt,
                arrival?.LandingUrl, arrival?.Referrer, arrival?.Utm?.Source, arrival?.Utm?.Medium, arrival?.Utm?.Campaign, arrival?.Utm?.Term, arrival?.Utm?.Content,
                arrival?.ClickIds?.Gclid, arrival?.ClickIds?.Gbraid, arrival?.ClickIds?.Wbraid, arrival?.ClickIds?.Fbclid, arrival?.ClickIds?.Msclkid, arrival?.ClickIds?.LiFatId, arrival?.ClickIds?.Ttclid,
                Num(env?.Screen?.Width), Num(env?.Screen?.Height), Num(env?.Screen?.AvailWidth), Num(env?.Screen?.AvailHeight), Num(env?.Screen?.ViewportWidth), Num(env?.Screen?.ViewportHeight), Num(env?.Screen?.DevicePixelRatio), Num(env?.Screen?.ColorDepth), env?.Screen?.Orientation,
                env?.Device?.Platform, Num(env?.Device?.HardwareConcurrency), Num(env?.Device?.DeviceMemory), Num(env?.Device?.MaxTouchPoints), env?.Device?.Pointer, Bool(env?.Device?.Hover),
                string.Join(' ', env?.Browser?.Languages ?? new List<string>()), env?.Browser?.Timezone, Num(env?.Browser?.UtcOffsetMinutes), Bool(env?.Browser?.CookieEnabled), Bool(env?.Browser?.PdfViewerEnabled), env?.Browser?.DisplayMode, env?.Browser?.ColorScheme, Bool(env?.Browser?.ReducedMotion),
                env?.HighEntropy?.PlatformVersion, env?.HighEntropy?.Model, env?.HighEntropy?.Architecture, env?.HighEntropy?.Bitness, string.Join(' ', env?.HighEntropy?.FullVersionList ?? new List<string>()),
                env?.Network?.EffectiveType, Num(env?.Network?.Downlink), Num(env?.Network?.Rtt), Bool(env?.Network?.SaveData),
                Num(s.Pages?.Count ?? 0), Num(ActiveSeconds(s)), Date(s.Scheduler?.ViewedAt), Date(s.Scheduler?.DatePickedAt), Date(s.Scheduler?.SlotHeldAt), Date(s.Scheduler?.BookedAt), Date(s.ContactSubmittedAt),
                Date(s.IntentReachedAt),
                profile?.Identity?.FirstName, profile?.Identity?.LastName, profile?.Identity?.Email, profile?.Identity?.Phone, profile?.Identity?.Source, Date(profile?.Identity?.IdentifiedAt),
                Date(profile?.FirstSeenAt), Num(profile?.SessionCount), Num(score30),
            });
        }
        return Encode(sb);
    }

    public static byte[] Pages(IEnumerable<VisitorSession> sessions)
    {
        var sb = new StringBuilder();
        Row(sb, PageColumns);
        foreach (var s in sessions)
        {
            foreach (var p in s.Pages ?? new List<VisitorPageView>())
            {
                Row(sb, new[]
                {
                    s.Id.ToString(), s.VisitorId.ToString(), p.PageViewId, p.Path, p.Query, p.Title, Date(p.EnteredAt), Num(p.ActiveMs), Num(p.MaxScrollPct), Num(p.Clicks?.Count ?? 0),
                    Num(p.Vitals?.Lcp), Num(p.Vitals?.Inp), Num(p.Vitals?.Cls), Num(p.Vitals?.Fcp), Num(p.Vitals?.Ttfb),
                });
            }
        }
        return Encode(sb);
    }

    public static byte[] Interactions(IEnumerable<VisitorSession> sessions)
    {
        var sb = new StringBuilder();
        Row(sb, InteractionColumns);
        foreach (var s in sessions)
        {
            var rows = new List<(DateTime At, string?[] Cells)>();
            foreach (var p in s.Pages ?? new List<VisitorPageView>())
            {
                foreach (var c in p.Clicks ?? new List<VisitorPageClick>())
                    rows.Add((c.At, new[] { s.Id.ToString(), s.VisitorId.ToString(), "click", Date(c.At), p.Path, c.Href, c.LinkId, Bool(c.Outbound) }));
            }
            AddStep(rows, s, "scheduler_view", s.Scheduler?.ViewedAt);
            AddStep(rows, s, "scheduler_date_picked", s.Scheduler?.DatePickedAt);
            AddStep(rows, s, "scheduler_slot_held", s.Scheduler?.SlotHeldAt);
            AddStep(rows, s, "scheduler_booked", s.Scheduler?.BookedAt);
            AddStep(rows, s, "contact_submitted", s.ContactSubmittedAt);
            foreach (var (_, cells) in rows.OrderBy(r => r.At))
                Row(sb, cells);
        }
        return Encode(sb);
    }

    private static void AddStep(List<(DateTime At, string?[] Cells)> rows, VisitorSession s, string kind, DateTime? at)
    {
        if (at is null) return;
        rows.Add((at.Value, new[] { s.Id.ToString(), s.VisitorId.ToString(), kind, Date(at), "", "", "", "" }));
    }

    public static long ActiveSeconds(VisitorSession s) =>
        (s.Pages ?? new List<VisitorPageView>()).Sum(p => Math.Max(0, p.ActiveMs)) / 1000;

    /// <summary>One row. Every cell is quoted when it holds a comma, a quote, or a line break.</summary>
    public static void Row(StringBuilder sb, IEnumerable<string?> cells)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Quote(cell));
        }
        sb.Append("\r\n");
    }

    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needs = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static byte[] Encode(StringBuilder sb)
    {
        var text = Encoding.UTF8.GetBytes(sb.ToString());
        var bom = Encoding.UTF8.GetPreamble();
        var bytes = new byte[bom.Length + text.Length];
        bom.CopyTo(bytes, 0);
        text.CopyTo(bytes, bom.Length);
        return bytes;
    }

    private static string Date(DateTime? value) =>
        value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Num(double? value) =>
        value is null ? string.Empty : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Num(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Bool(bool? value) => value is null ? string.Empty : (value.Value ? "true" : "false");
}
