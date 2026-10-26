using GeekApplication.Models.Visitors;

namespace GeekAPI.Services.Visitors;

/// <summary>The short phrases the report writes for a session: where, on what, and from where.</summary>
internal static class VisitorReportText
{
    internal static string? Place(VisitorSession session)
    {
        var parts = new[] { session.Geo?.City, session.Geo?.Region, session.Geo?.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    internal static string Device(VisitorSession session)
    {
        var ua = session.UserAgent;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ua?.DeviceType)) parts.Add(ua.DeviceType);
        if (!string.IsNullOrWhiteSpace(ua?.Brand) || !string.IsNullOrWhiteSpace(ua?.Model))
            parts.Add($"{ua?.Brand} {ua?.Model}".Trim());
        if (!string.IsNullOrWhiteSpace(ua?.Os))
            parts.Add(string.IsNullOrWhiteSpace(ua.OsVersion) ? ua.Os : $"{ua.Os} {ua.OsVersion}");
        if (!string.IsNullOrWhiteSpace(ua?.Browser))
            parts.Add(string.IsNullOrWhiteSpace(ua.BrowserVersion) ? ua.Browser : $"{ua.Browser} {ua.BrowserVersion}");
        return parts.Count == 0 ? "unknown" : string.Join(", ", parts);
    }

    internal static string? ReferrerHost(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)) return null;
        return Uri.TryCreate(referrer, UriKind.Absolute, out var uri) ? uri.Host : referrer;
    }

    internal static string? Utm(VisitorUtm? utm)
    {
        if (utm is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(utm.Source)) parts.Add("source " + utm.Source);
        if (!string.IsNullOrWhiteSpace(utm.Medium)) parts.Add("medium " + utm.Medium);
        if (!string.IsNullOrWhiteSpace(utm.Campaign)) parts.Add("campaign " + utm.Campaign);
        if (!string.IsNullOrWhiteSpace(utm.Term)) parts.Add("term " + utm.Term);
        if (!string.IsNullOrWhiteSpace(utm.Content)) parts.Add("content " + utm.Content);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
