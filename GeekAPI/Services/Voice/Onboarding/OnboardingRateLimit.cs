using System.Threading.RateLimiting;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// A cap on the routes that need no login: the signup routes, because each signup that passes the gate writes a tenant, and owner
/// sign-in, because a password can be guessed only as fast as this lets it. One
/// address may make <see cref="PermitsPerWindow"/> calls in <see cref="Window"/>; the next is answered 429 with code rate_limited
/// until the window ends. The address is the visitor's, forwarded by the portal (<see cref="OnboardingVisitor"/>), because the connection's
/// own address is the portal's and would put every visitor in one bucket. A call with no visitor address uses the connection's address, and one with neither shares one bucket, which makes it the first to be refused and never an unlimited one. Nothing waits in a
/// queue and nothing is retried.
/// </summary>
public static class OnboardingRateLimit
{
    public const string PolicyName = "onboarding";
    public const string Code = "rate_limited";
    public const int PermitsPerWindow = 10;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            OnboardingVisitor.From(context).IpAddress ?? context.Connection.RemoteIpAddress?.ToString() ?? "no-address",
            _ => new FixedWindowRateLimiterOptions
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
