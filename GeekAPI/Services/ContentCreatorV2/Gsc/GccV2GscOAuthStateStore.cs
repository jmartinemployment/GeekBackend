using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeekAPI.Services.ContentCreatorV2.Gsc;

public sealed record GccV2GscOAuthStatePayload(string Owner, string? SiteUrl, string? ReturnPath);

/// <summary>
/// Stateless HMAC-signed OAuth state so Google callbacks succeed on any GeekAPI instance.
/// Pattern copied from Geek-SEO SignedGoogleOAuthStateStore; uses GEEK_CC_GSC_ENCRYPTION_KEY.
/// </summary>
public sealed class GccV2GscOAuthStateStore
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public (string State, DateTimeOffset ExpiresAt) Create(GccV2GscOAuthStatePayload payload)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(StateTtl);
        var envelope = new StateEnvelope
        {
            Owner = payload.Owner,
            SiteUrl = string.IsNullOrWhiteSpace(payload.SiteUrl) ? null : payload.SiteUrl.Trim(),
            ReturnPath = string.IsNullOrWhiteSpace(payload.ReturnPath) ? null : payload.ReturnPath.Trim(),
            Exp = expiresAt.ToUnixTimeSeconds(),
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var signature = ComputeSignature(json);
        var state = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(json))}.{Base64UrlEncode(signature)}";
        return (state, expiresAt);
    }

    public GccV2GscOAuthStatePayload Consume(string state) => Parse(state, requireUnexpired: true);

    public bool TryPeek(string state, out GccV2GscOAuthStatePayload? payload)
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

    private static GccV2GscOAuthStatePayload Parse(string state, bool requireUnexpired)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new InvalidOperationException("Missing OAuth state.");

        var trimmed = state.Trim();
        var separator = trimmed.LastIndexOf('.');
        if (separator <= 0 || separator >= trimmed.Length - 1)
            throw new InvalidOperationException("OAuth state is invalid or expired. Restart Google connection.");

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(trimmed[..separator]);
            providedSignature = Base64UrlDecode(trimmed[(separator + 1)..]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("OAuth state is invalid or expired. Restart Google connection.");
        }

        var json = Encoding.UTF8.GetString(payloadBytes);
        var expectedSignature = ComputeSignature(json);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
            throw new InvalidOperationException("OAuth state is invalid or expired. Restart Google connection.");

        StateEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<StateEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Owner))
            throw new InvalidOperationException("OAuth state is invalid or expired. Restart Google connection.");

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(envelope.Exp);
        if (requireUnexpired && expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("OAuth state is invalid or expired. Restart Google connection.");

        return new GccV2GscOAuthStatePayload(envelope.Owner, envelope.SiteUrl, envelope.ReturnPath);
    }

    private static byte[] ComputeSignature(string json)
    {
        var key = GetSigningKey();
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
    }

    private static byte[] GetSigningKey()
    {
        var raw = Environment.GetEnvironmentVariable("GEEK_CC_GSC_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "GEEK_CC_GSC_ENCRYPTION_KEY must be set (base64-encoded 32-byte key) for GSC OAuth state signing.");
        }

        var key = Convert.FromBase64String(raw.Trim());
        if (key.Length != 32)
            throw new InvalidOperationException("GEEK_CC_GSC_ENCRYPTION_KEY must decode to exactly 32 bytes.");
        return key;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private sealed class StateEnvelope
    {
        public string Owner { get; init; } = string.Empty;
        public string? SiteUrl { get; init; }
        public string? ReturnPath { get; init; }
        public long Exp { get; init; }
        public string Nonce { get; init; } = string.Empty;
    }
}
