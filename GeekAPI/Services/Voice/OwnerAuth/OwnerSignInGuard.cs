using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace GeekAPI.Services.Voice.OwnerAuth;

/// <summary>
/// The cap on failed sign-ins per email, beside the per-address cap (OnboardingRateLimit), so a password cannot be guessed from many
/// addresses at once. An email that has failed <see cref="FailuresPerWindow"/> times in <see cref="Window"/> is refused without its
/// password being checked until the window ends, whether or not a login has that email, so the answer never tells which emails have one.
/// A sign-in carrying a device token issued to that same login (<see cref="ReceptionistTokens.DeviceTokenOwnerAsync"/>) is not held back,
/// so someone who knows an owner's email cannot keep the owner out of a browser the owner has signed in from. The count is kept in
/// GeekAPI's memory under the SHA-256 of the email, never the email itself, and starts again when GeekAPI restarts.
/// </summary>
public sealed class OwnerSignInGuard : IDisposable
{
    public const int FailuresPerWindow = 10;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly PartitionedRateLimiter<string> _failures = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = FailuresPerWindow,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    /// <summary>The key an email is counted under: trimmed and lower-cased, as logins are stored, then hashed.</summary>
    public static string KeyFor(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));

    /// <summary>True when the email has used up its failures for this window.</summary>
    public bool IsBlocked(string key)
    {
        using var lease = _failures.AttemptAcquire(key, 0);
        return !lease.IsAcquired;
    }

    public void RecordFailure(string key)
    {
        using var lease = _failures.AttemptAcquire(key, 1);
    }

    public void Dispose() => _failures.Dispose();
}
