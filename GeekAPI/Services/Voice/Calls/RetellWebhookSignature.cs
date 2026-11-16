using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>
/// Retell's webhook signature (docs.retellai.com, Secure the webhook): the X-Retell-Signature header is <c>v={ms},d={hex}</c>, where
/// <c>d</c> is HMAC-SHA256 of the raw body followed by the timestamp's digits, keyed by the Retell API key that carries the webhook badge.
/// A delivery is accepted only when the digest matches, compared in constant time, and the timestamp is within
/// <see cref="Tolerance"/> of now in either direction, so a recorded delivery cannot be replayed later.
/// </summary>
public static partial class RetellWebhookSignature
{
    public const string Header = "X-Retell-Signature";
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [GeneratedRegex(@"^v=(\d{1,19}),d=([0-9a-fA-F]{64})$")]
    private static partial Regex Format();

    public static bool IsValid(ReadOnlySpan<byte> body, string? header, string apiKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(apiKey)) return false;

        var match = Format().Match(header.Trim());
        if (!match.Success) return false;
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)) return false;

        if (Math.Abs(now.ToUnixTimeMilliseconds() - milliseconds) > (long)Tolerance.TotalMilliseconds) return false;

        return CryptographicOperations.FixedTimeEquals(Digest(body, match.Groups[1].Value, apiKey), Convert.FromHexString(match.Groups[2].Value));
    }

    /// <summary>The header Retell would send for this body at this time. Used by tests and by a check run against the live route.</summary>
    public static string Sign(ReadOnlySpan<byte> body, string apiKey, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        return $"v={timestamp},d={Convert.ToHexString(Digest(body, timestamp, apiKey)).ToLowerInvariant()}";
    }

    /// <summary>HMAC-SHA256 of the raw body followed by the timestamp's digits, keyed by the API key.</summary>
    private static byte[] Digest(ReadOnlySpan<byte> body, string timestamp, string apiKey)
    {
        var stamp = Encoding.ASCII.GetBytes(timestamp);
        var signed = new byte[body.Length + stamp.Length];
        body.CopyTo(signed);
        stamp.CopyTo(signed.AsSpan(body.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKey), signed);
    }
}
