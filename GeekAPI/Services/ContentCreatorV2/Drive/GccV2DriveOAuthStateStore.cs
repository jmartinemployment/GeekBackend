using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Drive;

public sealed record GccV2DriveOAuthStatePayload(string Owner, string? AccountLabel, string? ReturnPath);

/// <summary>Signed OAuth state for Drive connect (same HMAC pattern as GSC).</summary>
public sealed class GccV2DriveOAuthStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public (string State, DateTimeOffset ExpiresAt) Create(GccV2DriveOAuthStatePayload payload)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Ttl);
        var envelope = new Envelope(
            payload.Owner,
            payload.AccountLabel,
            payload.ReturnPath,
            expiresAt.ToUnixTimeSeconds());
        var json = JsonSerializer.Serialize(envelope);
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var sig = Sign(body);
        return ($"{body}.{sig}", expiresAt);
    }

    public GccV2DriveOAuthStatePayload Consume(string state) => Parse(state, requireUnexpired: true);

    public bool TryPeek(string state, out GccV2DriveOAuthStatePayload? payload)
    {
        try
        {
            payload = Parse(state, requireUnexpired: false);
            return true;
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    private static GccV2DriveOAuthStatePayload Parse(string state, bool requireUnexpired)
    {
        var parts = (state ?? "").Split('.', 2);
        if (parts.Length != 2)
            throw new InvalidOperationException("Drive OAuth state is malformed.");
        var body = parts[0];
        var sig = parts[1];
        if (!FixedTimeEquals(sig, Sign(body)))
            throw new InvalidOperationException("Drive OAuth state signature is invalid.");
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(body));
        var envelope = JsonSerializer.Deserialize<Envelope>(json)
            ?? throw new InvalidOperationException("Drive OAuth state payload is invalid.");
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(envelope.Exp);
        if (requireUnexpired && expiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Drive OAuth state expired. Start connect again.");
        if (string.IsNullOrWhiteSpace(envelope.Owner))
            throw new InvalidOperationException("Drive OAuth state is missing owner.");
        return new GccV2DriveOAuthStatePayload(envelope.Owner, envelope.AccountLabel, envelope.ReturnPath);
    }

    private static string Sign(string body)
    {
        var key = Convert.FromBase64String(
            (Environment.GetEnvironmentVariable("GEEK_CC_GSC_ENCRYPTION_KEY") ?? "").Trim());
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed record Envelope(string Owner, string? AccountLabel, string? ReturnPath, long Exp);
}
