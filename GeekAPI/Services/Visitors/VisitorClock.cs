using System.Globalization;

namespace GeekAPI.Services.Visitors;

/// <summary>New York days and windows. Every report boundary and every day key goes through here.</summary>
public static class VisitorClock
{
    public const string NewYorkZoneId = "America/New_York";

    /// <summary>
    /// The zone, by its IANA id. On a Windows host the same zone is registered under its Windows
    /// id, so that id is tried second; nothing else is tried, and a host with neither throws here.
    /// </summary>
    public static TimeZoneInfo NewYork { get; } = ResolveNewYork();

    private static TimeZoneInfo ResolveNewYork()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(NewYorkZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    public static DateTime ToNewYork(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), NewYork);

    public static DateOnly NewYorkDate(DateTime utc) => DateOnly.FromDateTime(ToNewYork(utc));

    public static string DayKey(DateTime utc) => NewYorkDate(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseDayKey(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>
    /// The UTC range [start, end) of one New York calendar day. Midnight always exists, so the
    /// window is 23 hours on the spring-forward day and 25 on the fall-back day.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayWindowUtc(DateOnly day)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), NewYork);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), NewYork);
        return (DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc));
    }

    /// <summary>"Fri 17 Oct 2026".</summary>
    public static string LongDay(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>A UTC instant written in New York time, "17 Oct 2026 14:05 ET".</summary>
    public static string InNewYork(DateTime utc) =>
        ToNewYork(utc).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " ET";

    /// <summary>
    /// A UTC instant in the visitor's own zone when the request named one the host knows, in New
    /// York time otherwise, with the zone written out either way.
    /// </summary>
    public static string InVisitorZone(DateTime utc, string? ianaZone)
    {
        if (!string.IsNullOrWhiteSpace(ianaZone))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaZone.Trim());
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
                return local.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " " + ianaZone.Trim();
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return InNewYork(utc);
    }
}
