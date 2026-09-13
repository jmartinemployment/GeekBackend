using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Signs trusted Knowledge index/delete envelopes for Geek-Crawler-Rag using the same
/// base64 HMAC keys as context manifest verification.
/// </summary>
public sealed class GccV2TrustedAssetSigner
{
    private readonly string _activeKeyId;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public GccV2TrustedAssetSigner(IConfiguration configuration)
    {
        var section = configuration.GetSection("ContentCreatorV2:ContextManifestSigning");
        _activeKeyId = section["ActiveKeyId"]?.Trim()
            ?? Environment.GetEnvironmentVariable("CONTEXT_MANIFEST_SIGNING_ACTIVE_KEY_ID")?.Trim()
            ?? "";
        var fromConfig = section.GetSection("Keys").GetChildren()
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key, x => Convert.FromBase64String(x.Value!), StringComparer.Ordinal);
        if (fromConfig.Count > 0)
        {
            _keys = fromConfig;
            return;
        }

        var raw = Environment.GetEnvironmentVariable("CONTEXT_MANIFEST_SIGNING_KEYS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            return;
        }

        using var doc = JsonDocument.Parse(raw);
        _keys = doc.RootElement.EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => Convert.FromBase64String(p.Value.GetString()!),
                StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(_activeKeyId) && _keys.Count == 1)
            _activeKeyId = _keys.Keys.First();
    }

    public string ActiveKeyId => _activeKeyId;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_activeKeyId)
        && _keys.TryGetValue(_activeKeyId, out var key)
        && key.Length >= 32;

    public TrustedAssetAuthEnvelope CreateEnvelope(
        string ownerUserId,
        string assetVersionId,
        string resourceId,
        string contentBinding)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Context manifest signing keys are required to call trusted Knowledge index/delete.");

        var nonce = Guid.NewGuid().ToString("N");
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var expiresUnix = expires.ToUnixTimeSeconds().ToString();
        var line = string.Join('|',
            "geekapi", nonce, expiresUnix, ownerUserId, assetVersionId, resourceId, contentBinding);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)))
            .ToLowerInvariant();
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(_keys[_activeKeyId], Encoding.ASCII.GetBytes(digest)))
            .ToLowerInvariant();
        return new TrustedAssetAuthEnvelope(
            "geekapi", nonce, expires, _activeKeyId, signature);
    }
}

public sealed record TrustedAssetAuthEnvelope(
    string CallerIdentity,
    string Nonce,
    DateTimeOffset ExpiresAtUtc,
    string SigningKeyId,
    string Signature);
