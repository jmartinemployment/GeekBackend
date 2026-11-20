using System.Security.Cryptography;
using GeekAPI.Services.Voice;

namespace GeekBackend.Tests.Voice;

public class VoiceCredentialProtectorTests
{
    private const string Purpose = VoiceSecretPurposes.McpAccessToken;

    private const string Secret = "fake-twilio-auth-token-0123456789abcdef";

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not base64!", false)]
    public void A_key_that_is_absent_or_malformed_is_not_usable(string? raw, bool usable)
    {
        Assert.Equal(usable, new VoiceCredentialProtector(() => raw).KeyIsUsable());
    }

    [Fact]
    public void A_key_of_exactly_32_bytes_is_usable_and_a_short_one_is_not()
    {
        var key = NewKey();

        Assert.True(new VoiceCredentialProtector(() => key).KeyIsUsable());
        Assert.False(new VoiceCredentialProtector(() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))).KeyIsUsable());
    }

    [Fact]
    public void A_secret_round_trips()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);

        var sealedSecret = protector.Protect(Secret, Purpose);
        Assert.True(sealedSecret.IsSuccess, sealedSecret.Error);
        var opened = protector.Unprotect(sealedSecret.Value!, Purpose);

        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(Secret, opened.Value);
    }

    [Fact]
    public void The_stored_text_does_not_contain_the_secret_and_every_encryption_uses_a_new_nonce()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);

        var first = protector.Protect(Secret, Purpose).Value!;
        var second = protector.Protect(Secret, Purpose).Value!;

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(Secret, first, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Secret)), first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_key_cannot_read_what_another_key_wrote()
    {
        var sealedSecret = new VoiceCredentialProtector(NewKey).Protect(Secret, Purpose).Value!;

        var opened = new VoiceCredentialProtector(NewKey).Unprotect(sealedSecret, Purpose);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
    }

    [Fact]
    public void A_changed_byte_is_refused()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);
        var bytes = Convert.FromBase64String(protector.Protect(Secret, Purpose).Value!);
        bytes[^1] ^= 0x01;

        var opened = protector.Unprotect(Convert.ToBase64String(bytes), Purpose);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void A_missing_or_malformed_key_fails_both_ways_and_never_stores_plain_text(string? key)
    {
        var protector = new VoiceCredentialProtector(() => key);

        var protectedSecret = protector.Protect(Secret, Purpose);

        // Well formed as far as its format byte goes, so what fails is the key and not the layout: a zero-filled blob would now be
        // refused as an unknown format before the key was ever read, and this test would stop being about the key.
        var wellFormed = new byte[40];
        wellFormed[0] = 1;
        var opened = protector.Unprotect(Convert.ToBase64String(wellFormed), Purpose);

        Assert.False(protectedSecret.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, protectedSecret.ErrorCode);
        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var result = new VoiceCredentialProtector(() => key).Protect(Secret, Purpose);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_secret_is_refused(string? secret)
    {
        var key = NewKey();

        var result = new VoiceCredentialProtector(() => key).Protect(secret!, Purpose);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.InvalidSecret, result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("AAAA")]
    public void A_stored_value_that_is_not_well_formed_is_refused(string? stored)
    {
        var key = NewKey();

        var result = new VoiceCredentialProtector(() => key).Unprotect(stored!, Purpose);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.InvalidSecret, result.ErrorCode);
    }

    [Fact]
    public void No_failure_message_carries_the_secret_or_the_key()
    {
        var key = NewKey();
        var other = NewKey();
        var sealedSecret = new VoiceCredentialProtector(() => key).Protect(Secret, Purpose).Value!;

        var failures = new[]
        {
            new VoiceCredentialProtector(() => other).Unprotect(sealedSecret, Purpose),
            new VoiceCredentialProtector(() => null).Protect(Secret, Purpose),
        };

        foreach (var failure in failures)
        {
            Assert.DoesNotContain(Secret, failure.Error!, StringComparison.Ordinal);
            Assert.DoesNotContain(key, failure.Error!, StringComparison.Ordinal);
            Assert.DoesNotContain(other, failure.Error!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_value_sealed_for_one_purpose_cannot_be_opened_for_another()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = new VoiceCredentialProtector(() => key);
        var sealedAsState = protector.Protect(Secret, VoiceSecretPurposes.McpOAuthState).Value!;

        Assert.Equal(Secret, protector.Unprotect(sealedAsState, VoiceSecretPurposes.McpOAuthState).Value);
        foreach (var other in new[] { VoiceSecretPurposes.McpAccessToken, VoiceSecretPurposes.McpRefreshToken, VoiceSecretPurposes.TwilioAuthToken, VoiceSecretPurposes.KeyCheck })
        {
            var opened = protector.Unprotect(sealedAsState, other);
            Assert.False(opened.IsSuccess, other);
            Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_purpose_is_a_programming_error_and_throws(string purpose)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = new VoiceCredentialProtector(() => key);
        Assert.Throws<ArgumentException>(() => protector.Protect(Secret, purpose));
        Assert.Throws<ArgumentException>(() => protector.Unprotect("anything", purpose));
    }
}
