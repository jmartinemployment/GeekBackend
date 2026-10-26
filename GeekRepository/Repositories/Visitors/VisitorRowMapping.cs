using GeekApplication.Models.Visitors;
using GeekRepository.Data.Entities.Visitors;

namespace GeekRepository.Repositories.Visitors;

/// <summary>
/// Rows to records and back. A nested object of a record is present when any of its columns is,
/// and absent (null) when none is, so a session reads back as it was written.
/// </summary>
internal static class VisitorRowMapping
{
    public static VisitorSessionRow NewSession(VisitorPageViewCommand c, DateTime now, int visitNumber)
    {
        var geo = c.Geo ?? new VisitorGeo();
        var ua = c.UserAgent ?? new VisitorUserAgent();
        var row = new VisitorSessionRow
        {
            Id = c.SessionId,
            VisitorId = c.VisitorId,
            VisitNumber = visitNumber,
            StartedAt = now,
            LastSeenAt = now,
            Ip = c.Ip ?? string.Empty,
            AdditionalIps = new List<string>(),
            EdgeRegion = c.EdgeRegion,
            Florida = c.Florida,
            GeoCountry = geo.Country,
            GeoRegion = geo.Region,
            GeoCity = geo.City,
            GeoPostalCode = geo.PostalCode,
            GeoLatitude = geo.Latitude,
            GeoLongitude = geo.Longitude,
            GeoTimezone = geo.Timezone,
            GeoContinent = geo.Continent,
            UaRaw = ua.Raw ?? string.Empty,
            UaBrowser = ua.Browser,
            UaBrowserVersion = ua.BrowserVersion,
            UaOs = ua.Os,
            UaOsVersion = ua.OsVersion,
            UaDeviceType = ua.DeviceType,
            UaBrand = ua.Brand,
            UaModel = ua.Model,
            ChUa = c.ClientHints?.Ua,
            ChMobile = c.ClientHints?.Mobile,
            ChPlatform = c.ClientHints?.Platform,
            AcceptLanguage = c.AcceptLanguage,
            Dnt = c.Dnt,
            Score = 0,
        };
        ApplyEnvironment(row, c.Snapshot?.Environment);
        ApplyArrival(row, c.Snapshot?.Arrival);
        return row;
    }

    public static VisitorPageRow NewPage(Guid sessionId, int seq, VisitorPageView page, DateTime enteredAt)
    {
        var row = new VisitorPageRow
        {
            SessionId = sessionId,
            PageViewId = page.PageViewId,
            Seq = seq,
            Path = page.Path,
            Query = page.Query,
            Title = page.Title,
            EnteredAt = enteredAt,
            ActiveMs = Math.Max(0, page.ActiveMs),
            MaxScrollPct = Math.Clamp(page.MaxScrollPct, 0, 100),
            Lcp = page.Vitals?.Lcp,
            Inp = page.Vitals?.Inp,
            Cls = page.Vitals?.Cls,
            Fcp = page.Vitals?.Fcp,
            Ttfb = page.Vitals?.Ttfb,
        };
        var clickSeq = 0;
        foreach (var click in page.Clicks ?? new List<VisitorPageClick>())
            row.Clicks.Add(NewClick(sessionId, page.PageViewId, clickSeq++, click));
        return row;
    }

    public static VisitorClickRow NewClick(Guid sessionId, string pageViewId, int seq, VisitorPageClick click) => new()
    {
        SessionId = sessionId,
        PageViewId = pageViewId,
        Seq = seq,
        Href = click.Href,
        LinkId = click.LinkId,
        Outbound = click.Outbound,
        ClickedAt = VisitorTime.Normalize(click.At),
    };

    public static VisitorSession ToRecord(VisitorSessionRow r) => new()
    {
        Id = r.Id,
        Sequence = r.RowNo,
        VisitorId = r.VisitorId,
        VisitNumber = r.VisitNumber,
        StartedAt = Utc(r.StartedAt),
        LastSeenAt = Utc(r.LastSeenAt),
        Ip = r.Ip,
        AdditionalIps = r.AdditionalIps is null ? new List<string>() : new List<string>(r.AdditionalIps),
        Geo = new VisitorGeo
        {
            Country = r.GeoCountry,
            Region = r.GeoRegion,
            City = r.GeoCity,
            PostalCode = r.GeoPostalCode,
            Latitude = r.GeoLatitude,
            Longitude = r.GeoLongitude,
            Timezone = r.GeoTimezone,
            Continent = r.GeoContinent,
        },
        BrowserGeo = r.BrowserGeoLatitude is { } lat && r.BrowserGeoLongitude is { } lng && r.BrowserGeoAt is { } at
            ? new VisitorBrowserGeo { Latitude = lat, Longitude = lng, AccuracyMeters = r.BrowserGeoAccuracyMeters, At = Utc(at) }
            : null,
        EdgeRegion = r.EdgeRegion,
        Florida = r.Florida,
        UserAgent = new VisitorUserAgent
        {
            Raw = r.UaRaw,
            Browser = r.UaBrowser,
            BrowserVersion = r.UaBrowserVersion,
            Os = r.UaOs,
            OsVersion = r.UaOsVersion,
            DeviceType = r.UaDeviceType,
            Brand = r.UaBrand,
            Model = r.UaModel,
        },
        ClientHints = r.ChUa is null && r.ChMobile is null && r.ChPlatform is null
            ? null
            : new VisitorClientHints { Ua = r.ChUa, Mobile = r.ChMobile, Platform = r.ChPlatform },
        AcceptLanguage = r.AcceptLanguage,
        Dnt = r.Dnt,
        Environment = ReadEnvironment(r),
        Arrival = ReadArrival(r),
        Pages = r.Pages.OrderBy(p => p.Seq).Select(ToRecord).ToList(),
        Scheduler = r.SchedulerViewedAt is null && r.SchedulerDatePickedAt is null && r.SchedulerSlotHeldAt is null && r.SchedulerBookedAt is null
            ? null
            : new VisitorSchedulerFunnel
            {
                ViewedAt = Utc(r.SchedulerViewedAt),
                DatePickedAt = Utc(r.SchedulerDatePickedAt),
                SlotHeldAt = Utc(r.SchedulerSlotHeldAt),
                BookedAt = Utc(r.SchedulerBookedAt),
            },
        ContactSubmittedAt = Utc(r.ContactSubmittedAt),
        Score = r.Score,
        IntentReachedAt = Utc(r.IntentReachedAt),
    };

    public static VisitorPageView ToRecord(VisitorPageRow p) => new()
    {
        PageViewId = p.PageViewId,
        Path = p.Path,
        Query = p.Query,
        Title = p.Title,
        EnteredAt = Utc(p.EnteredAt),
        ActiveMs = p.ActiveMs,
        MaxScrollPct = p.MaxScrollPct,
        Vitals = p.Lcp is null && p.Inp is null && p.Cls is null && p.Fcp is null && p.Ttfb is null
            ? null
            : new VisitorWebVitals { Lcp = p.Lcp, Inp = p.Inp, Cls = p.Cls, Fcp = p.Fcp, Ttfb = p.Ttfb },
        Clicks = p.Clicks.OrderBy(c => c.Seq).Select(c => new VisitorPageClick
        {
            Href = c.Href,
            LinkId = c.LinkId,
            Outbound = c.Outbound,
            At = Utc(c.ClickedAt),
        }).ToList(),
    };

    public static VisitorProfile ToRecord(VisitorRow r) => new()
    {
        Id = r.Id,
        FirstSeenAt = Utc(r.FirstSeenAt),
        LastSeenAt = Utc(r.LastSeenAt),
        SessionCount = r.SessionCount,
        Identity = r.IdentityEmail is null && r.IdentifiedAt is null
            ? null
            : new VisitorIdentity
            {
                FirstName = r.IdentityFirstName ?? string.Empty,
                LastName = r.IdentityLastName ?? string.Empty,
                Email = r.IdentityEmail ?? string.Empty,
                Phone = r.IdentityPhone ?? string.Empty,
                Source = r.IdentitySource ?? string.Empty,
                IdentifiedAt = Utc(r.IdentifiedAt) ?? default,
            },
    };

    public static VisitorDailyReport ToRecord(VisitorDailyReportRow r, IReadOnlyDictionary<string, int> drops) => new()
    {
        Id = r.Day,
        ClaimedAt = Utc(r.ClaimedAt),
        BuiltAt = Utc(r.BuiltAt),
        Totals = r.BuiltAt is null
            ? null
            : new VisitorReportTotals
            {
                Visitors = r.Visitors,
                NewVisitors = r.NewVisitors,
                ReturningVisitors = r.ReturningVisitors,
                Sessions = r.Sessions,
                PageViews = r.PageViews,
                MedianActiveSeconds = r.MedianActiveSeconds,
                FloridaSessions = r.FloridaSessions,
                IdentifiedVisitors = r.IdentifiedVisitors,
                IntentSessions = r.IntentSessions,
                Drops = new Dictionary<string, int>(drops),
            },
        Purge = r.PurgedAt is null
            ? null
            : new VisitorPurgeOutcome
            {
                At = Utc(r.PurgedAt.Value),
                Sessions = r.PurgedSessions,
                Visitors = r.PurgedVisitors,
                Error = r.PurgeError,
            },
    };

    public static void ApplyPurge(VisitorDailyReportRow row, VisitorPurgeOutcome? purge)
    {
        row.PurgedAt = purge is null ? null : VisitorTime.Normalize(purge.At);
        row.PurgedSessions = purge?.Sessions;
        row.PurgedVisitors = purge?.Visitors;
        row.PurgeError = string.IsNullOrWhiteSpace(purge?.Error) ? null : purge!.Error!.Trim();
    }

    public static void ApplyTotals(VisitorDailyReportRow row, VisitorReportTotals totals)
    {
        row.Visitors = totals.Visitors;
        row.NewVisitors = totals.NewVisitors;
        row.ReturningVisitors = totals.ReturningVisitors;
        row.Sessions = totals.Sessions;
        row.PageViews = totals.PageViews;
        row.MedianActiveSeconds = totals.MedianActiveSeconds;
        row.FloridaSessions = totals.FloridaSessions;
        row.IdentifiedVisitors = totals.IdentifiedVisitors;
        row.IntentSessions = totals.IntentSessions;
    }

    private static void ApplyEnvironment(VisitorSessionRow row, VisitorEnvironment? env)
    {
        if (env is null) return;
        if (env.Screen is { } s)
        {
            row.ScreenWidth = s.Width;
            row.ScreenHeight = s.Height;
            row.ScreenAvailWidth = s.AvailWidth;
            row.ScreenAvailHeight = s.AvailHeight;
            row.ViewportWidth = s.ViewportWidth;
            row.ViewportHeight = s.ViewportHeight;
            row.DevicePixelRatio = s.DevicePixelRatio;
            row.ColorDepth = s.ColorDepth;
            row.Orientation = s.Orientation;
        }
        if (env.Device is { } d)
        {
            row.DevicePlatform = d.Platform;
            row.HardwareConcurrency = d.HardwareConcurrency;
            row.DeviceMemory = d.DeviceMemory;
            row.MaxTouchPoints = d.MaxTouchPoints;
            row.Pointer = d.Pointer;
            row.Hover = d.Hover;
        }
        if (env.Browser is { } b)
        {
            row.Languages = new List<string>(b.Languages ?? new List<string>());
            row.BrowserTimezone = b.Timezone;
            row.UtcOffsetMinutes = b.UtcOffsetMinutes;
            row.CookieEnabled = b.CookieEnabled;
            row.PdfViewerEnabled = b.PdfViewerEnabled;
            row.DisplayMode = b.DisplayMode;
            row.ColorScheme = b.ColorScheme;
            row.ReducedMotion = b.ReducedMotion;
        }
        if (env.HighEntropy is { } h)
        {
            row.PlatformVersion = h.PlatformVersion;
            row.HintModel = h.Model;
            row.Architecture = h.Architecture;
            row.Bitness = h.Bitness;
            row.FullVersionList = new List<string>(h.FullVersionList ?? new List<string>());
        }
        if (env.Network is { } n)
        {
            row.EffectiveType = n.EffectiveType;
            row.Downlink = n.Downlink;
            row.Rtt = n.Rtt;
            row.SaveData = n.SaveData;
        }
    }

    private static void ApplyArrival(VisitorSessionRow row, VisitorArrival? arrival)
    {
        if (arrival is null) return;
        row.LandingUrl = arrival.LandingUrl;
        row.Referrer = arrival.Referrer;
        if (arrival.Utm is { } u)
        {
            row.UtmSource = u.Source;
            row.UtmMedium = u.Medium;
            row.UtmCampaign = u.Campaign;
            row.UtmTerm = u.Term;
            row.UtmContent = u.Content;
        }
        if (arrival.ClickIds is { } c)
        {
            row.Gclid = c.Gclid;
            row.Gbraid = c.Gbraid;
            row.Wbraid = c.Wbraid;
            row.Fbclid = c.Fbclid;
            row.Msclkid = c.Msclkid;
            row.LiFatId = c.LiFatId;
            row.Ttclid = c.Ttclid;
        }
    }

    private static VisitorEnvironment? ReadEnvironment(VisitorSessionRow r)
    {
        var screen = r.ScreenWidth is null && r.ScreenHeight is null && r.ScreenAvailWidth is null && r.ScreenAvailHeight is null
            && r.ViewportWidth is null && r.ViewportHeight is null && r.DevicePixelRatio is null && r.ColorDepth is null && r.Orientation is null
            ? null
            : new VisitorScreen
            {
                Width = r.ScreenWidth,
                Height = r.ScreenHeight,
                AvailWidth = r.ScreenAvailWidth,
                AvailHeight = r.ScreenAvailHeight,
                ViewportWidth = r.ViewportWidth,
                ViewportHeight = r.ViewportHeight,
                DevicePixelRatio = r.DevicePixelRatio,
                ColorDepth = r.ColorDepth,
                Orientation = r.Orientation,
            };
        var device = r.DevicePlatform is null && r.HardwareConcurrency is null && r.DeviceMemory is null
            && r.MaxTouchPoints is null && r.Pointer is null && r.Hover is null
            ? null
            : new VisitorDevice
            {
                Platform = r.DevicePlatform,
                HardwareConcurrency = r.HardwareConcurrency,
                DeviceMemory = r.DeviceMemory,
                MaxTouchPoints = r.MaxTouchPoints,
                Pointer = r.Pointer,
                Hover = r.Hover,
            };
        var browser = r.Languages is null && r.BrowserTimezone is null && r.UtcOffsetMinutes is null && r.CookieEnabled is null
            && r.PdfViewerEnabled is null && r.DisplayMode is null && r.ColorScheme is null && r.ReducedMotion is null
            ? null
            : new VisitorBrowserInfo
            {
                Languages = r.Languages is null ? new List<string>() : new List<string>(r.Languages),
                Timezone = r.BrowserTimezone,
                UtcOffsetMinutes = r.UtcOffsetMinutes,
                CookieEnabled = r.CookieEnabled,
                PdfViewerEnabled = r.PdfViewerEnabled,
                DisplayMode = r.DisplayMode,
                ColorScheme = r.ColorScheme,
                ReducedMotion = r.ReducedMotion,
            };
        var highEntropy = r.PlatformVersion is null && r.HintModel is null && r.Architecture is null && r.Bitness is null && r.FullVersionList is null
            ? null
            : new VisitorHighEntropyHints
            {
                PlatformVersion = r.PlatformVersion,
                Model = r.HintModel,
                Architecture = r.Architecture,
                Bitness = r.Bitness,
                FullVersionList = r.FullVersionList is null ? new List<string>() : new List<string>(r.FullVersionList),
            };
        var network = r.EffectiveType is null && r.Downlink is null && r.Rtt is null && r.SaveData is null
            ? null
            : new VisitorNetwork { EffectiveType = r.EffectiveType, Downlink = r.Downlink, Rtt = r.Rtt, SaveData = r.SaveData };

        if (screen is null && device is null && browser is null && highEntropy is null && network is null) return null;
        return new VisitorEnvironment { Screen = screen, Device = device, Browser = browser, HighEntropy = highEntropy, Network = network };
    }

    private static VisitorArrival? ReadArrival(VisitorSessionRow r)
    {
        var utm = r.UtmSource is null && r.UtmMedium is null && r.UtmCampaign is null && r.UtmTerm is null && r.UtmContent is null
            ? null
            : new VisitorUtm { Source = r.UtmSource, Medium = r.UtmMedium, Campaign = r.UtmCampaign, Term = r.UtmTerm, Content = r.UtmContent };
        var clickIds = r.Gclid is null && r.Gbraid is null && r.Wbraid is null && r.Fbclid is null && r.Msclkid is null && r.LiFatId is null && r.Ttclid is null
            ? null
            : new VisitorClickIds
            {
                Gclid = r.Gclid,
                Gbraid = r.Gbraid,
                Wbraid = r.Wbraid,
                Fbclid = r.Fbclid,
                Msclkid = r.Msclkid,
                LiFatId = r.LiFatId,
                Ttclid = r.Ttclid,
            };
        if (r.LandingUrl is null && r.Referrer is null && utm is null && clickIds is null) return null;
        return new VisitorArrival { LandingUrl = r.LandingUrl, Referrer = r.Referrer, Utm = utm, ClickIds = clickIds };
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
