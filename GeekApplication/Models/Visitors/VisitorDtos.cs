namespace GeekApplication.Models.Visitors;

// The hops: the site posts a VisitorPulseEvent to GeekAPI, GeekAPI posts the commands below to
// GeekRepository, and GeekRepository answers with the records in VisitorRecords.cs.

public static class VisitorEventKinds
{
    public const string PageView = "page_view";
    public const string PageEnd = "page_end";
    public const string Click = "click";
    public const string Scheduler = "scheduler";
    public const string Contact = "contact";
    public const string Vitals = "vitals";
    public const string Geo = "geo";

    public static readonly IReadOnlyList<string> All =
        new[] { PageView, PageEnd, Click, Scheduler, Contact, Vitals, Geo };
}

public static class VisitorSchedulerSteps
{
    public const string View = "view";
    public const string DatePicked = "date_picked";
    public const string SlotHeld = "slot_held";
    public const string Booked = "booked";

    public static readonly IReadOnlyList<string> All = new[] { View, DatePicked, SlotHeld, Booked };
}

public static class VisitorVitalNames
{
    public static readonly IReadOnlyList<string> All = new[] { "LCP", "INP", "CLS", "FCP", "TTFB" };
}

public static class VisitorIdentitySources
{
    public const string Booking = "booking";
    public const string Contact = "contact";
}

/// <summary>Why the site's /api/pulse dropped a request. The only keys filter_stats accepts.</summary>
public static class VisitorDropReasons
{
    public const string TooLarge = "too_large";
    public const string CrossSite = "cross_site";
    public const string Bot = "bot";
    public const string GlobalPrivacyControl = "gpc";
    public const string NoIp = "no_ip";
    public const string ExcludedIp = "excluded_ip";
    public const string InvalidBody = "invalid_body";

    public static readonly IReadOnlyList<string> All =
        new[] { TooLarge, CrossSite, Bot, GlobalPrivacyControl, NoIp, ExcludedIp, InvalidBody };

    public static bool IsKnown(string? reason) => reason is not null && All.Contains(reason);
}

/// <summary>What the site forwards for one accepted beacon: the browser's event and the request's fields.</summary>
public sealed class VisitorPulseEvent
{
    public string Kind { get; set; } = string.Empty;
    public Guid VisitorId { get; set; }
    public Guid SessionId { get; set; }
    public string? PageViewId { get; set; }
    public string? Path { get; set; }
    public string? Query { get; set; }
    public string? Title { get; set; }
    public VisitorSessionSnapshot? Snapshot { get; set; }
    public long? ActiveMs { get; set; }
    public int? MaxScrollPct { get; set; }
    public VisitorClickEvent? Click { get; set; }
    public string? SchedulerStep { get; set; }
    public VisitorVital? Vital { get; set; }
    public VisitorBrowserPosition? Position { get; set; }
    public VisitorRequestFields Request { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
}

/// <summary>Collected once per session by the browser and sent with its first page view.</summary>
public sealed class VisitorSessionSnapshot
{
    public VisitorArrival? Arrival { get; set; }
    public VisitorEnvironment? Environment { get; set; }
}

public sealed class VisitorClickEvent
{
    public string? Href { get; set; }
    public string? LinkId { get; set; }
    public bool Outbound { get; set; }
}

public sealed class VisitorVital
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
}

public sealed class VisitorBrowserPosition
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
}

/// <summary>Read from the request by the site's /api/pulse route.</summary>
public sealed class VisitorRequestFields
{
    public string Ip { get; set; } = string.Empty;
    public VisitorGeo Geo { get; set; } = new();
    public string? EdgeRegion { get; set; }
    public string UserAgent { get; set; } = string.Empty;
    public VisitorClientHints? ClientHints { get; set; }
    public string? AcceptLanguage { get; set; }
    public string? Dnt { get; set; }
}

public sealed class VisitorIdentifyRequest
{
    public Guid VisitorId { get; set; }
    public Guid? SessionId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class VisitorDropRequest
{
    public string Reason { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}

// GeekAPI -> GeekRepository commands.

public sealed class VisitorPageViewCommand
{
    public Guid SessionId { get; set; }
    public Guid VisitorId { get; set; }
    public VisitorPageView Page { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
    public string Ip { get; set; } = string.Empty;
    public VisitorGeo Geo { get; set; } = new();
    public string? EdgeRegion { get; set; }
    public bool Florida { get; set; }
    public VisitorUserAgent UserAgent { get; set; } = new();
    public VisitorClientHints? ClientHints { get; set; }
    public string? AcceptLanguage { get; set; }
    public string? Dnt { get; set; }
    public VisitorSessionSnapshot? Snapshot { get; set; }
}

public sealed class VisitorPageEndCommand
{
    public string PageViewId { get; set; } = string.Empty;
    public long ActiveMs { get; set; }
    public int MaxScrollPct { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public sealed class VisitorClickCommand
{
    public string PageViewId { get; set; } = string.Empty;
    public VisitorPageClick Click { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
}

public sealed class VisitorVitalsCommand
{
    public string PageViewId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
}

public sealed class VisitorSchedulerStepCommand
{
    public string Step { get; set; } = string.Empty;
    public DateTime At { get; set; }
}

public sealed class VisitorContactCommand
{
    public DateTime At { get; set; }
}

public sealed class VisitorBrowserGeoCommand
{
    public VisitorBrowserGeo Position { get; set; } = new();
    /// <summary>True when the position falls inside Florida and the request carried no region.</summary>
    public bool MarkFlorida { get; set; }
}

public sealed class VisitorScoreCommand
{
    public double Score { get; set; }
}

/// <summary>A Florida session reached the intent threshold. Lands once: a second mark is a no-op.</summary>
public sealed class VisitorIntentReachedCommand
{
    public DateTime At { get; set; }
}

public sealed class VisitorIdentifyCommand
{
    public Guid VisitorId { get; set; }
    public Guid? SessionId { get; set; }
    public VisitorIdentity Identity { get; set; } = new();
}

public sealed class VisitorDropCommand
{
    public string Day { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class VisitorDailyReportClaimCommand
{
    public DateTime ClaimedAt { get; set; }
}

public sealed class VisitorDailyReportBuiltCommand
{
    public DateTime BuiltAt { get; set; }
    public VisitorReportTotals Totals { get; set; } = new();
    public VisitorPurgeOutcome? Purge { get; set; }
}

public sealed class VisitorDailyReportReleaseCommand
{
    public DateTime ClaimedAt { get; set; }
}

public sealed class VisitorClaimResult
{
    public bool Claimed { get; set; }
}

/// <summary>Remove every session and visitor last seen more than the retention period before <see cref="AsOf"/>.</summary>
public sealed class VisitorPurgeCommand
{
    public DateTime AsOf { get; set; }
}

public sealed class VisitorPurgeResult
{
    public int Sessions { get; set; }
    public int Visitors { get; set; }
}

public sealed class VisitorProfileIdsRequest
{
    public List<Guid> Ids { get; set; } = new();
}

/// <summary>UTC instants as the store keeps them: UTC kind, millisecond precision.</summary>
public static class VisitorTime
{
    public static DateTime Normalize(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    public static DateTime Normalize(DateTimeOffset value) => Normalize(value.UtcDateTime);
}
