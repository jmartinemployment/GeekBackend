using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.Social;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

public class SocialCredentialProtectorTests
{
    private const string TokenValue = "EAAB-this-is-a-fake-access-token-value";

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_token_round_trips_and_keeps_its_expiry()
    {
        var key = NewKey();
        var protector = new SocialCredentialProtector(() => key);
        var expires = new DateTime(2027, 1, 8, 12, 0, 0, DateTimeKind.Utc);

        var sealedToken = protector.Protect(new SocialToken(TokenValue, expires));
        Assert.True(sealedToken.IsSuccess, sealedToken.Error);
        Assert.Equal(expires, sealedToken.Value!.ExpiresAt);
        Assert.Equal(12, sealedToken.Value.Iv.Length);
        Assert.Equal(16, sealedToken.Value.Tag.Length);

        var opened = protector.Unprotect(sealedToken.Value);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(TokenValue, opened.Value!.Reveal());
        Assert.Equal(expires, opened.Value.ExpiresAt);
    }

    [Fact]
    public void The_stored_bytes_do_not_contain_the_token_and_every_encryption_uses_a_new_nonce()
    {
        var key = NewKey();
        var protector = new SocialCredentialProtector(() => key);
        var token = new SocialToken(TokenValue);

        var first = protector.Protect(token).Value!;
        var second = protector.Protect(token).Value!;

        Assert.DoesNotContain(TokenValue, Encoding.UTF8.GetString(first.Cipher), StringComparison.Ordinal);
        Assert.NotEqual(first.Iv, second.Iv);
        Assert.NotEqual(first.Cipher, second.Cipher);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all!")]
    [InlineData("AAAA")]
    public void A_missing_or_malformed_key_fails_loudly_without_throwing(string? key)
    {
        var protector = new SocialCredentialProtector(() => key);

        var sealedToken = protector.Protect(new SocialToken(TokenValue));
        Assert.False(sealedToken.IsSuccess);
        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, sealedToken.ErrorCode);
        Assert.Contains(SocialCredentialProtector.KeyVariable, sealedToken.Error, StringComparison.Ordinal);

        var opened = protector.Unprotect(new SocialEncryptedToken { Cipher = new byte[] { 1 }, Iv = new byte[12], Tag = new byte[16] });
        Assert.False(opened.IsSuccess);
        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, opened.ErrorCode);
    }

    [Fact]
    public void A_key_that_is_not_32_bytes_is_refused_even_when_it_is_valid_base64()
    {
        var short16 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var long48 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, new SocialCredentialProtector(() => short16).Protect(new SocialToken(TokenValue)).ErrorCode);
        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, new SocialCredentialProtector(() => long48).Protect(new SocialToken(TokenValue)).ErrorCode);
    }

    [Fact]
    public void A_token_sealed_under_another_key_or_altered_in_the_database_is_not_returned()
    {
        var keyA = NewKey();
        var keyB = NewKey();
        var sealedUnderA = new SocialCredentialProtector(() => keyA).Protect(new SocialToken(TokenValue)).Value!;

        var wrongKey = new SocialCredentialProtector(() => keyB).Unprotect(sealedUnderA);
        Assert.False(wrongKey.IsSuccess);
        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, wrongKey.ErrorCode);
        Assert.DoesNotContain(TokenValue, wrongKey.Error, StringComparison.Ordinal);

        sealedUnderA.Cipher[0] ^= 0xFF;
        var altered = new SocialCredentialProtector(() => keyA).Unprotect(sealedUnderA);
        Assert.False(altered.IsSuccess);
        Assert.Equal(SocialFailureCodes.EncryptionUnavailable, altered.ErrorCode);
    }

    [Fact]
    public void A_stored_token_that_is_not_well_formed_is_an_invalid_request()
    {
        var protector = new SocialCredentialProtector(NewKey);

        Assert.Equal(SocialFailureCodes.InvalidRequest, protector.Unprotect(new SocialEncryptedToken { Cipher = Array.Empty<byte>(), Iv = new byte[12], Tag = new byte[16] }).ErrorCode);
        Assert.Equal(SocialFailureCodes.InvalidRequest, protector.Unprotect(new SocialEncryptedToken { Cipher = new byte[] { 1 }, Iv = new byte[8], Tag = new byte[16] }).ErrorCode);
        Assert.Equal(SocialFailureCodes.InvalidRequest, protector.Unprotect(new SocialEncryptedToken { Cipher = new byte[] { 1 }, Iv = new byte[12], Tag = new byte[4] }).ErrorCode);
    }
}
