using System.Security.Cryptography;
using GeekAPI.Services.Voice;

namespace GeekBackend.Tests.Voice;

public class VoiceCredentialProtectorTests
{
    private const string Secret = "fake-twilio-auth-token-0123456789abcdef";

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_secret_round_trips()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);

        var sealedSecret = protector.Protect(Secret);
        Assert.True(sealedSecret.IsSuccess, sealedSecret.Error);
        var opened = protector.Unprotect(sealedSecret.Value!);

        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(Secret, opened.Value);
    }

    [Fact]
    public void The_stored_text_does_not_contain_the_secret_and_every_encryption_uses_a_new_nonce()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);

        var first = protector.Protect(Secret).Value!;
        var second = protector.Protect(Secret).Value!;

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(Secret, first, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Secret)), first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_key_cannot_read_what_another_key_wrote()
    {
        var sealedSecret = new VoiceCredentialProtector(NewKey).Protect(Secret).Value!;

        var opened = new VoiceCredentialProtector(NewKey).Unprotect(sealedSecret);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
    }

    [Fact]
    public void A_changed_byte_is_refused()
    {
        var key = NewKey();
        var protector = new VoiceCredentialProtector(() => key);
        var bytes = Convert.FromBase64String(protector.Protect(Secret).Value!);
        bytes[^1] ^= 0x01;

        var opened = protector.Unprotect(Convert.ToBase64String(bytes));

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

        var protectedSecret = protector.Protect(Secret);
        var stored = Convert.ToBase64String(new byte[40]);
        var opened = protector.Unprotect(stored);

        Assert.False(protectedSecret.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, protectedSecret.ErrorCode);
        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, opened.ErrorCode);
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var result = new VoiceCredentialProtector(() => key).Protect(Secret);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_secret_is_refused(string? secret)
    {
        var key = NewKey();

        var result = new VoiceCredentialProtector(() => key).Protect(secret!);

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

        var result = new VoiceCredentialProtector(() => key).Unprotect(stored!);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.InvalidSecret, result.ErrorCode);
    }

    [Fact]
    public void No_failure_message_carries_the_secret_or_the_key()
    {
        var key = NewKey();
        var other = NewKey();
        var sealedSecret = new VoiceCredentialProtector(() => key).Protect(Secret).Value!;

        var failures = new[]
        {
            new VoiceCredentialProtector(() => other).Unprotect(sealedSecret),
            new VoiceCredentialProtector(() => null).Protect(Secret),
        };

        foreach (var failure in failures)
        {
            Assert.DoesNotContain(Secret, failure.Error!, StringComparison.Ordinal);
            Assert.DoesNotContain(key, failure.Error!, StringComparison.Ordinal);
            Assert.DoesNotContain(other, failure.Error!, StringComparison.Ordinal);
        }
    }
}
