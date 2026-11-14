using System.Net;
using GeekAPI.Auth;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>Where a signup came from: the visitor's own address and the country Vercel resolved for it.</summary>
public sealed record VisitorOrigin(string? IpAddress, string? CountryCode);

/// <summary>
/// The visitor's address and country, as the Geek-Receptionist-Portal forwards them. The portal is a server that stands between the
/// visitor and GeekAPI, so the address GeekAPI sees on the connection is the portal's, never the visitor's. The portal sends the
/// visitor's address in <see cref="IpHeader"/> and Vercel's country for it (the x-vercel-ip-country value) in <see cref="CountryHeader"/>.
/// Both are read only from a call whose X-API-Key ApiKeyMiddleware verified; from any other caller they read as absent, so a header
/// a visitor typed himself is never believed.
/// </summary>
public static class OnboardingVisitor
{
    public const string IpHeader = "X-Visitor-Ip";
    public const string CountryHeader = "X-Visitor-Country";

    public static VisitorOrigin From(HttpContext context)
    {
        if (!context.Items.TryGetValue(CrmAuthConstants.VerifiedApiKeyItem, out var verified) || verified is not true)
            return new VisitorOrigin(null, null);

        string? ip = null;
        if (IPAddress.TryParse(context.Request.Headers[IpHeader].ToString().Trim(), out var parsed))
            ip = (parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed).ToString();

        var country = context.Request.Headers[CountryHeader].ToString().Trim();
        return new VisitorOrigin(ip, country.Length == 0 ? null : country);
    }
}
