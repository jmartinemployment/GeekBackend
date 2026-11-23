using System.Security.Cryptography;
using GeekApplication.Security;
using GeekRepository.Auth;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GeekBackend.Tests.Security;

/// <summary>
/// The refusals an independent review of the token check asked to have pinned: another algorithm or curve, a token whose time claims
/// are missing or in the future, a key on a 256-bit curve that is not P-256, and the replay guard outliving the token it guards.
/// </summary>
public sealed class GeekRepositoryTokenHardeningTests
{
    private static (ECDsa Private, GeekApiTokenValidator Validator) NewValidator()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        var time = TimeProvider.System;
        return (key, new GeekApiTokenValidator(new ECDsaSecurityKey(publicKey), new TokenReplayGuard(time), time));
    }

    private static string SignedPayload(ECDsa key, string payloadJson) =>
        new JsonWebTokenHandler().CreateToken(payloadJson, new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256));

    private static string Payload(string jti, long? iat, long? nbf, long? exp)
    {
        var parts = new List<string> { "\"iss\":\"geek-api\"", "\"aud\":\"geek-repository\"", $"\"jti\":\"{jti}\"" };
        if (iat is not null) parts.Add($"\"iat\":{iat}");
        if (nbf is not null) parts.Add($"\"nbf\":{nbf}");
        if (exp is not null) parts.Add($"\"exp\":{exp}");
        return "{" + string.Join(",", parts) + "}";
    }

    [Fact]
    public async Task A_token_signed_with_another_curve_or_by_hmac_with_the_public_key_is_refused()
    {
        var (key, validator) = NewValidator();
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var es384 = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = GeekRepositoryToken.Issuer,
            Audience = GeekRepositoryToken.Audience,
            Expires = DateTime.UtcNow.AddSeconds(60),
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Jti] = "es384" },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(p384), SecurityAlgorithms.EcdsaSha384),
        });
        Assert.False((await validator.CheckAsync(es384)).IsValid);

        // The classic algorithm-confusion token: HS256 with the public key's bytes as the shared secret.
        var confused = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = GeekRepositoryToken.Issuer,
            Audience = GeekRepositoryToken.Audience,
            Expires = DateTime.UtcNow.AddSeconds(60),
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Jti] = "hs256" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key.ExportSubjectPublicKeyInfo()), SecurityAlgorithms.HmacSha256),
        });
        Assert.False((await validator.CheckAsync(confused)).IsValid);
    }

    [Fact]
    public async Task A_signed_token_with_a_missing_or_future_time_claim_is_refused()
    {
        var (key, validator) = NewValidator();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var later = now + 3600;

        var noExp = await validator.CheckAsync(SignedPayload(key, Payload("n1", now, null, null)));
        var noIat = await validator.CheckAsync(SignedPayload(key, Payload("n2", null, null, now + 60)));
        var futureIat = await validator.CheckAsync(SignedPayload(key, Payload("n3", later, null, later + 60)));
        var futureNbf = await validator.CheckAsync(SignedPayload(key, Payload("n4", now, later, later + 60)));
        var ok = await validator.CheckAsync(SignedPayload(key, Payload("n5", now, now, now + 60)));

        Assert.False(noExp.IsValid);
        Assert.False(noIat.IsValid);
        Assert.Contains("issue time", noIat.Reason, StringComparison.Ordinal);
        Assert.False(futureIat.IsValid);
        Assert.False(futureNbf.IsValid);
        Assert.True(ok.IsValid, ok.Reason);
    }

    [Fact]
    public void A_256_bit_key_on_another_curve_is_not_p256()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.True(GeekRepositoryToken.IsP256(p256));
        Assert.False(GeekRepositoryToken.IsP256(p384));

        try
        {
            using var brainpool = ECDsa.Create(ECCurve.NamedCurves.brainpoolP256r1);
            Assert.False(GeekRepositoryToken.IsP256(brainpool));
        }
        catch (PlatformNotSupportedException)
        {
            // This platform has no brainpool curves, so there is no such key to present to it.
        }
        catch (CryptographicException)
        {
            // Same: the platform refuses to create the curve.
        }
    }

    [Fact]
    public void The_replay_guard_remembers_an_id_past_the_moment_its_token_stops_validating()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var guard = new TokenReplayGuard(clock);
        var expires = clock.GetUtcNow() + TimeSpan.FromSeconds(60);
        Assert.True(guard.TryRegister("id", expires));

        // Exp plus one skew is the last instant the token validates; a purge then runs. The id must survive it.
        clock.Advance(TimeSpan.FromSeconds(60) + GeekRepositoryToken.ClockSkew + TimeSpan.FromSeconds(1));
        Assert.True(guard.TryRegister("another", clock.GetUtcNow() + TimeSpan.FromSeconds(60)));
        Assert.False(guard.TryRegister("id", expires));
    }
}
