namespace GeekApplication.Models.Visitors;

// The visitor records as GeekAPI reads and writes them over HTTP: the contract between GeekAPI and
// GeekRepository's repo/visitors/* routes. GeekRepository stores them as rows of the geek_visitors
// schema in its own Postgres (GeekRepository/Data/Entities/Visitors) and maps between the two, so
// a nested object here is a group of columns there. Timestamps are DateTime in UTC at millisecond
// precision (VisitorTime.Normalize), which is what timestamptz holds.

/// <summary>One visit. <see cref="Id"/> is the session UUID the browser minted.</summary>
public sealed class VisitorSession
{
    public Guid Id { get; set; }
    /// <summary>The store's insertion order: the paging cursor of the session list, never a business value.</summary>
    public long Sequence { get; set; }
    public Guid VisitorId { get; set; }
    public int VisitNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public string Ip { get; set; } = string.Empty;
    public List<string> AdditionalIps { get; set; } = new();
    public VisitorGeo Geo { get; set; } = new();
    public VisitorBrowserGeo? BrowserGeo { get; set; }
    public string? EdgeRegion { get; set; }
    public bool Florida { get; set; }

    public VisitorUserAgent UserAgent { get; set; } = new();
    public VisitorClientHints? ClientHints { get; set; }
    public string? AcceptLanguage { get; set; }
    public string? Dnt { get; set; }
    public VisitorEnvironment? Environment { get; set; }
    public VisitorArrival? Arrival { get; set; }

    public List<VisitorPageView> Pages { get; set; } = new();
    public VisitorSchedulerFunnel? Scheduler { get; set; }
    public DateTime? ContactSubmittedAt { get; set; }
    public double Score { get; set; }
    /// <summary>When a Florida session first reached the intent threshold. Set once; nothing is sent.</summary>
    public DateTime? IntentReachedAt { get; set; }
}

/// <summary>One browser. <see cref="Id"/> is the persistent visitor UUID.</summary>
public sealed class VisitorProfile
{
    public Guid Id { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int SessionCount { get; set; }
    public VisitorIdentity? Identity { get; set; }
}

/// <summary>Drops per reason for one New York day. <see cref="Id"/> is the day, yyyy-MM-dd.</summary>
public sealed class VisitorFilterDay
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, int> Counts { get; set; } = new();
}

/// <summary>
/// The claim and the outcome of one day's report build. <see cref="Id"/> is the New York date.
/// The report itself is composed on demand from the day's sessions; this row records that the
/// 07:00 build ran, and its totals, which carry no visitor data and so outlive the sessions.
/// </summary>
public sealed class VisitorDailyReport
{
    public string Id { get; set; } = string.Empty;
    public DateTime ClaimedAt { get; set; }
    public DateTime? BuiltAt { get; set; }
    public VisitorReportTotals? Totals { get; set; }
    /// <summary>The retention purge the build ran, with its error when it failed. Null until the day is built.</summary>
    public VisitorPurgeOutcome? Purge { get; set; }
}

/// <summary>
/// What the retention purge did when the day was built: the counts it removed, or the error the
/// store answered. Recorded on the day's row so a failed purge is read from the summary, not from
/// a log.
/// </summary>
public sealed class VisitorPurgeOutcome
{
    public DateTime At { get; set; }
    public int? Sessions { get; set; }
    public int? Visitors { get; set; }
    public string? Error { get; set; }
}

public sealed class VisitorGeo
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Timezone { get; set; }
    public string? Continent { get; set; }
}

/// <summary>
/// A position the browser's geolocation API gave, asked for only when the request carried no
/// location. Recorded as the browser reported it, with its accuracy radius.
/// </summary>
public sealed class VisitorBrowserGeo
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTime At { get; set; }
}

public sealed class VisitorUserAgent
{
    public string Raw { get; set; } = string.Empty;
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public string? Os { get; set; }
    public string? OsVersion { get; set; }
    public string? DeviceType { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
}

public sealed class VisitorClientHints
{
    public string? Ua { get; set; }
    public string? Mobile { get; set; }
    public string? Platform { get; set; }
}

public sealed class VisitorEnvironment
{
    public VisitorScreen? Screen { get; set; }
    public VisitorDevice? Device { get; set; }
    public VisitorBrowserInfo? Browser { get; set; }
    public VisitorHighEntropyHints? HighEntropy { get; set; }
    public VisitorNetwork? Network { get; set; }
}

public sealed class VisitorScreen
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? AvailWidth { get; set; }
    public int? AvailHeight { get; set; }
    public int? ViewportWidth { get; set; }
    public int? ViewportHeight { get; set; }
    public double? DevicePixelRatio { get; set; }
    public int? ColorDepth { get; set; }
    public string? Orientation { get; set; }
}

public sealed class VisitorDevice
{
    public string? Platform { get; set; }
    public int? HardwareConcurrency { get; set; }
    public double? DeviceMemory { get; set; }
    public int? MaxTouchPoints { get; set; }
    public string? Pointer { get; set; }
    public bool? Hover { get; set; }
}

public sealed class VisitorBrowserInfo
{
    public List<string> Languages { get; set; } = new();
    public string? Timezone { get; set; }
    public int? UtcOffsetMinutes { get; set; }
    public bool? CookieEnabled { get; set; }
    public bool? PdfViewerEnabled { get; set; }
    public string? DisplayMode { get; set; }
    public string? ColorScheme { get; set; }
    public bool? ReducedMotion { get; set; }
}

public sealed class VisitorHighEntropyHints
{
    public string? PlatformVersion { get; set; }
    public string? Model { get; set; }
    public string? Architecture { get; set; }
    public string? Bitness { get; set; }
    public List<string> FullVersionList { get; set; } = new();
}

public sealed class VisitorNetwork
{
    public string? EffectiveType { get; set; }
    public double? Downlink { get; set; }
    public int? Rtt { get; set; }
    public bool? SaveData { get; set; }
}

public sealed class VisitorArrival
{
    public string? LandingUrl { get; set; }
    public string? Referrer { get; set; }
    public VisitorUtm? Utm { get; set; }
    public VisitorClickIds? ClickIds { get; set; }
}

public sealed class VisitorUtm
{
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Term { get; set; }
    public string? Content { get; set; }
}

public sealed class VisitorClickIds
{
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? Fbclid { get; set; }
    public string? Msclkid { get; set; }
    public string? LiFatId { get; set; }
    public string? Ttclid { get; set; }
}

public sealed class VisitorPageView
{
    public string PageViewId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? Title { get; set; }
    public DateTime EnteredAt { get; set; }
    public long ActiveMs { get; set; }
    public int MaxScrollPct { get; set; }
    public VisitorWebVitals? Vitals { get; set; }
    public List<VisitorPageClick> Clicks { get; set; } = new();
}

public sealed class VisitorWebVitals
{
    public double? Lcp { get; set; }
    public double? Inp { get; set; }
    public double? Cls { get; set; }
    public double? Fcp { get; set; }
    public double? Ttfb { get; set; }
}

public sealed class VisitorPageClick
{
    public string? Href { get; set; }
    public string? LinkId { get; set; }
    public bool Outbound { get; set; }
    public DateTime At { get; set; }
}

public sealed class VisitorSchedulerFunnel
{
    public DateTime? ViewedAt { get; set; }
    public DateTime? DatePickedAt { get; set; }
    public DateTime? SlotHeldAt { get; set; }
    public DateTime? BookedAt { get; set; }
}

public sealed class VisitorIdentity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime IdentifiedAt { get; set; }
}

public sealed class VisitorReportTotals
{
    public int Visitors { get; set; }
    public int NewVisitors { get; set; }
    public int ReturningVisitors { get; set; }
    public int Sessions { get; set; }
    public int PageViews { get; set; }
    public int MedianActiveSeconds { get; set; }
    public int FloridaSessions { get; set; }
    public int IdentifiedVisitors { get; set; }
    /// <summary>Florida sessions that reached the intent threshold.</summary>
    public int IntentSessions { get; set; }
    public Dictionary<string, int> Drops { get; set; } = new();
}
