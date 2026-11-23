using System.Security.Cryptography;
using GeekAPI.HttpClients;
using GeekApplication.Security;
using GeekRepository.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GeekBackend.Tests.Security;

/// <summary>
/// GeekRepository answers only GeekAPI because a request must carry a token GeekAPI signed (Jeff, 2026-11-23). These tests are the
/// refusals: a token that is unsigned, signed by anyone else, for another audience, from another issuer, expired, minted with a long
/// life, without an id, or presented twice is not accepted.
/// </summary>
public sealed class GeekRepositoryTokenTests
{
    private sealed class Pair
    {
        public Pair()
        {
            Private = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Time = TimeProvider.System;
            Signer = new GeekRepositoryTokenSigner(Private, Time);
            Validator = new GeekApiTokenValidator(new ECDsaSecurityKey(Public(Private)), new TokenReplayGuard(Time), Time);
        }

        public ECDsa Private { get; }
        public TimeProvider Time { get; }
        public GeekRepositoryTokenSigner Signer { get; }
        public GeekApiTokenValidator Validator { get; }
    }

    private static ECDsa Public(ECDsa privateKey)
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(privateKey.ExportSubjectPublicKeyInfo(), out _);
        return publicKey;
    }

    private static string Mint(
        ECDsa key,
        string issuer = GeekRepositoryToken.Issuer,
        string audience = GeekRepositoryToken.Audience,
        TimeSpan? issuedAgo = null,
        TimeSpan? lifetime = null,
        string? jti = "an-id",
        string algorithm = SecurityAlgorithms.EcdsaSha256)
    {
        var issued = DateTime.UtcNow - (issuedAgo ?? TimeSpan.Zero);
        var claims = new Dictionary<string, object>();
        if (jti is not null)
            claims[JwtRegisteredClaimNames.Jti] = jti;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issued,
            NotBefore = issued,
            Expires = issued + (lifetime ?? TimeSpan.FromSeconds(60)),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key), algorithm),
        });
    }

    [Fact]
    public async Task A_token_GeekAPI_minted_is_accepted()
    {
        var p = new Pair();
        var check = await p.Validator.CheckAsync(p.Signer.Create());
        Assert.True(check.IsValid, check.Reason);
    }

    [Fact]
    public async Task The_same_token_presented_twice_is_refused_the_second_time()
    {
        var p = new Pair();
        var token = p.Signer.Create();

        Assert.True((await p.Validator.CheckAsync(token)).IsValid);
        var second = await p.Validator.CheckAsync(token);

        Assert.False(second.IsValid);
        Assert.Contains("already been used", second.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_token_GeekAPI_mints_has_its_own_id()
    {
        var p = new Pair();
        Assert.True((await p.Validator.CheckAsync(p.Signer.Create())).IsValid);
        Assert.True((await p.Validator.CheckAsync(p.Signer.Create())).IsValid);
    }

    [Fact]
    public async Task A_token_signed_by_any_other_key_is_refused()
    {
        var p = new Pair();
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False((await p.Validator.CheckAsync(Mint(stranger))).IsValid);
    }

    [Fact]
    public async Task A_token_for_another_audience_or_from_another_issuer_is_refused()
    {
        var p = new Pair();
        Assert.False((await p.Validator.CheckAsync(Mint(p.Private, audience: "geek-oauth", jti: "a1"))).IsValid);
        Assert.False((await p.Validator.CheckAsync(Mint(p.Private, issuer: "someone-else", jti: "a2"))).IsValid);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var p = new Pair();
        var expired = Mint(p.Private, issuedAgo: TimeSpan.FromMinutes(10), lifetime: TimeSpan.FromSeconds(60), jti: "old");
        Assert.False((await p.Validator.CheckAsync(expired)).IsValid);
    }

    [Fact]
    public async Task A_correctly_signed_token_minted_with_a_long_life_is_refused()
    {
        var p = new Pair();
        var longLived = Mint(p.Private, lifetime: TimeSpan.FromHours(1), jti: "long");
        var check = await p.Validator.CheckAsync(longLived);

        Assert.False(check.IsValid);
        Assert.Contains("longer life", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_with_no_id_is_refused()
    {
        var p = new Pair();
        var check = await p.Validator.CheckAsync(Mint(p.Private, jti: null));

        Assert.False(check.IsValid);
        Assert.Contains("no id", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unsigned_token_and_garbage_are_refused()
    {
        var p = new Pair();
        var unsigned = new JsonWebTokenHandler().CreateToken(
            """{"iss":"geek-api","aud":"geek-repository","jti":"u","exp":9999999999}""");

        Assert.False((await p.Validator.CheckAsync(unsigned)).IsValid);
        Assert.False((await p.Validator.CheckAsync("not-a-token")).IsValid);
        Assert.False((await p.Validator.CheckAsync("")).IsValid);
    }

    [Fact]
    public void A_signing_key_that_is_not_p256_is_refused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<InvalidOperationException>(() => new GeekRepositoryTokenSigner(p384, TimeProvider.System));
    }
}
