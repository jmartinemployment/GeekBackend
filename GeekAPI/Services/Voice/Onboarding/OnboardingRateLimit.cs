using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// The caps on the routes that need no login. <see cref="PolicyName"/> covers the signup routes, because each signup that passes the
/// gate writes a tenant. <see cref="SignInPolicyName"/> covers owner sign-in alone, so a new owner who used up the signup allowance can
/// still sign in, and a password can be guessed only as fast as it lets one address. Each policy lets one address make
/// <see cref="PermitsPerWindow"/> calls in <see cref="Window"/>, counted apart from the other policy; the next is answered 429 with code
/// rate_limited until the window ends. The address is the visitor's, forwarded by the portal (<see cref="OnboardingVisitor"/>), because
/// the connection's own address is the portal's and would put every visitor in one bucket. An IPv6 address counts as its /64, the block
/// one household or one server is given, so stepping through the addresses of that block is still one address. A call with no visitor
/// address uses the connection's address, and one with neither shares one bucket, which makes it the first to be refused and never an
/// unlimited one. Nothing waits in a queue and nothing is retried.
/// </summary>
public static class OnboardingRateLimit
{
    public const string PolicyName = "onboarding";
    public const string SignInPolicyName = "owner-sign-in";
    public const string Code = "rate_limited";
    public const int PermitsPerWindow = 10;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public static RateLimitPartition<string> Partition(HttpContext context) => Fixed(AddressKey(context));

    public static RateLimitPartition<string> SignInPartition(HttpContext context) => Fixed(AddressKey(context));

    /// <summary>The bucket a call counts against: the visitor's address, an IPv6 one cut to its /64.</summary>
    public static string AddressKey(HttpContext context)
    {
        if (OnboardingVisitor.From(context).IpAddress is { } visitor && IPAddress.TryParse(visitor, out var forwarded))
            return Key(forwarded);
        return context.Connection.RemoteIpAddress is { } connection ? Key(connection) : "no-address";
    }

    private static string Key(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static RateLimitPartition<string> Fixed(string key) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerWindow,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    public static async ValueTask WriteRejection(HttpContext context, CancellationToken ct)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(
            new { error = "Too many attempts from this address. Try again later.", code = Code }, ct);
    }
}
