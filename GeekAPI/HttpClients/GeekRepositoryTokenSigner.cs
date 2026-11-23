using System.Security.Cryptography;
using GeekApplication.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeekAPI.HttpClients;

/// <summary>
/// Mints the token GeekAPI presents to GeekRepository on every request: ES256, issuer <c>geek-api</c>, audience <c>geek-repository</c>,
/// valid for 60 seconds, with a fresh id so it can be used once. Holds the private key; nothing else in the system does.
/// </summary>
public sealed class GeekRepositoryTokenSigner
{
    private readonly ECDsa _key;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TimeProvider _time;
    private readonly object _signLock = new();

    public GeekRepositoryTokenSigner(ECDsa privateKey, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.KeySize != 256)
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.SigningKeyVariable} is a {privateKey.KeySize}-bit key; GeekRepository accepts P-256 (ES256) only.");
        }

        _key = privateKey;
        _time = time;
        _credentials = new SigningCredentials(new ECDsaSecurityKey(_key), GeekRepositoryToken.Algorithm);
    }

    /// <summary>Reads <see cref="GeekRepositoryToken.SigningKeyVariable"/> and refuses to build a signer from a missing or malformed key.</summary>
    public static GeekRepositoryTokenSigner FromEnvironment(TimeProvider time)
    {
        var text = Environment.GetEnvironmentVariable(GeekRepositoryToken.SigningKeyVariable);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.SigningKeyVariable} is not set. It is the key GeekAPI signs every GeekRepository request with, "
                + "and a client built without it could not prove who it is.");
        }

        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(text.Trim()), out _);
            return new GeekRepositoryTokenSigner(key, time);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.SigningKeyVariable} is not a base64 PKCS#8 private key ({ex.GetType().Name}).", ex);
        }
    }

    public string Create()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = GeekRepositoryToken.Issuer,
            Audience = GeekRepositoryToken.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + GeekRepositoryToken.Lifetime,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N") },
            SigningCredentials = _credentials,
        };

        // One ECDsa instance is shared by every request, and a signature on it is not documented as safe to run concurrently.
        lock (_signLock)
        {
            return _handler.CreateToken(descriptor);
        }
    }
}

/// <summary>Adds the token to every request the repository clients send.</summary>
public sealed class GeekRepositoryTokenHandler : DelegatingHandler
{
    private readonly GeekRepositoryTokenSigner _signer;

    public GeekRepositoryTokenHandler(GeekRepositoryTokenSigner signer) => _signer = signer;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _signer.Create());
        return base.SendAsync(request, cancellationToken);
    }
}
