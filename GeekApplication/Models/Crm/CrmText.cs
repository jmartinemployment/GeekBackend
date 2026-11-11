using System.Text.RegularExpressions;

namespace GeekApplication.Models.Crm;

/// <summary>Normalizing rules both sides of repo/crm/* apply the same way.</summary>
public static partial class CrmText
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex E164Regex();

    public static bool IsE164(string? value) => value is not null && E164Regex().IsMatch(value);

    /// <summary>
    /// "+1 855-473-4778", "'+1 (561) 555-0100", "5615550100" to E.164. US/Canada numbers without a
    /// country code are assumed +1. Anything else that is not already E.164 returns null.
    /// </summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var plus = raw.TrimStart('\'', ' ').StartsWith('+');
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        if (plus) return IsE164("+" + digits) ? "+" + digits : null;
        if (digits.Length == 10) return "+1" + digits;
        if (digits.Length == 11 && digits[0] == '1') return "+" + digits;
        return null;
    }

    /// <summary>"+15618656990" to "(561) 865-6990" for +1 numbers; others unchanged.</summary>
    public static string FormatPhone(string e164) =>
        e164.Length == 12 && e164.StartsWith("+1", StringComparison.Ordinal)
            ? $"({e164[2..5]}) {e164[5..8]}-{e164[8..]}"
            : e164;

    public static string? Email(string? raw)
    {
        var v = Trim(raw)?.ToLowerInvariant();
        return v is not null && v.Contains('@') ? v : null;
    }

    public static string? Trim(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>"https://www.firstresponseplumbers.com/" to "firstresponseplumbers.com".</summary>
    public static string? Domain(string? url)
    {
        var v = Trim(url);
        if (v is null) return null;
        if (!v.Contains("://", StringComparison.Ordinal)) v = "https://" + v;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// Whether two websites are the same site: both normalize to a domain and the domains are equal.
    /// acme.com is not notacme.com, acme.com.au or sub.acme.com; www. and the scheme do not count.
    /// </summary>
    public static bool SameDomain(string? a, string? b)
    {
        var left = Domain(a);
        return left is not null && string.Equals(left, Domain(b), StringComparison.Ordinal);
    }

    /// <summary>The first code of a list like "238220, 236118" when it has the expected digit count.</summary>
    public static string? FirstCode(string? raw, int digits)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var first = raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && first.Length == digits && first.All(char.IsDigit) ? first : null;
    }

    /// <summary>A title like "Owner", "President & CEO" or "Office Manager" to a link role.</summary>
    public static string RoleFromTitle(string? title)
    {
        var t = title?.ToLowerInvariant() ?? string.Empty;
        if (t.Contains("owner") || t.Contains("founder") || t.Contains("president") || t.Contains("ceo") || t.Contains("principal"))
            return ParticipantLinkRoles.Owner;
        return ParticipantLinkRoles.Employee;
    }
}
