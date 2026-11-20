using System.Security.Cryptography;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The four personal details this product stores, and the two rules that moved up to GeekAPI because GeekRepository can no longer
/// read them. Jeff, 2026-11-20: <i>"The application is a SaaS application, the variables are client data, shouldn't they be
/// encrypted?"</i>
///
/// <para>
/// <b>What is being pinned.</b> A number that is not a number and an address that is not an address are refused <i>here</i>, which
/// used to be <c>ValidateConfig</c>'s and <c>ValidateCreateTenant</c>'s job and cannot be any more: once the column holds one base64
/// string there is nothing there to match against E.164 or to find an @ in. The rule moved rather than being copied, so this is the
/// only place it is tested, and the repository's own tests say so where the rows were removed.
/// </para>
///
/// <para>
/// And the thing a purpose exists for: a value sealed as one kind of secret must not open as another. The bucket keys in
/// <c>storage_connections</c> were sealed under the Google OAuth purposes, so an access token and an AWS access key were
/// interchangeable ciphertext. Each of the eleven purposes is pinned against all the others below.
/// </para>
/// </summary>
public sealed class VoicePersonalDetailsTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static VoiceCredentialProtector WithKey(string key) => new(() => key);

    // ----- The shape rules that moved -----

    [Theory]
    [InlineData("+15615550137")]
    [InlineData("+442079460958")]
    public void A_number_in_e164_is_sealed(string phone)
    {
        var protector = WithKey(NewKey());

        var sealedNumber = VoicePersonalDetails.SealPhone(
            protector, phone, VoiceSecretPurposes.ConfigTransferTargetNumber, "not a number");

        Assert.True(sealedNumber.IsSuccess);
        Assert.NotEqual(phone, sealedNumber.Value);
        Assert.Equal(phone, protector.Unprotect(sealedNumber.Value!, VoiceSecretPurposes.ConfigTransferTargetNumber).Value);
    }

    /// <summary>Refused before anything is sealed, so a value that cannot be dialled is never a row that looks stored correctly.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("call me")]
    [InlineData("561-555-0137")]
    [InlineData("15615550137")]
    [InlineData("+1 561 555 0137")]
    public void A_value_that_is_not_an_e164_number_is_refused_and_never_sealed(string? phone)
    {
        var refused = VoicePersonalDetails.SealPhone(
            WithKey(NewKey()), phone, VoiceSecretPurposes.ConfigTransferTargetNumber, "not a number");

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Null(refused.Value);
    }

    [Fact]
    public void An_address_is_sealed_and_opens_to_itself()
    {
        var protector = WithKey(NewKey());

        var sealedEmail = VoicePersonalDetails.SealEmail(
            protector, "owner@acme-plumbing.example", VoiceSecretPurposes.TenantContactEmail, "not an address");

        Assert.True(sealedEmail.IsSuccess);
        Assert.DoesNotContain("acme-plumbing", sealedEmail.Value!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "owner@acme-plumbing.example",
            protector.Unprotect(sealedEmail.Value!, VoiceSecretPurposes.TenantContactEmail).Value);
    }

    /// <summary>
    /// The round-trip rule GeekRepository used, kept exactly: an address MailAddress accepts but rewrites is not the address somebody
    /// typed, and a message sent to it later would go somewhere else.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("Owner <owner@b.example>")]
    [InlineData("owner@b.example, other@b.example")]
    public void A_value_that_is_not_an_address_is_refused_and_never_sealed(string? email)
    {
        var refused = VoicePersonalDetails.SealEmail(
            WithKey(NewKey()), email, VoiceSecretPurposes.ConfigMessageDestinationEmail, "not an address");

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Null(refused.Value);
    }

    /// <summary>
    /// An unusable key refuses the write rather than letting a personal detail through in the clear. This is the fail-closed half: a
    /// business whose number could not be sealed is told, and no row is written.
    /// </summary>
    [Fact]
    public void With_no_key_a_personal_detail_is_refused_rather_than_stored_bare()
    {
        var noKey = new VoiceCredentialProtector(() => null);

        var phone = VoicePersonalDetails.SealPhone(
            noKey, "+15615550137", VoiceSecretPurposes.TenantContactPhone, "not a number");
        var email = VoicePersonalDetails.SealEmail(
            noKey, "owner@b.example", VoiceSecretPurposes.TenantContactEmail, "not an address");

        Assert.False(phone.IsSuccess);
        Assert.False(email.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, phone.ErrorCode);
        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, email.ErrorCode);
    }

    /// <summary>Nothing stored is not a blank address: reading it is a failure, so nothing sends to "".</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Opening_a_detail_that_was_never_stored_is_a_failure_and_not_a_blank_value(string? stored)
    {
        var opened = VoicePersonalDetails.Open(WithKey(NewKey()), stored, VoiceSecretPurposes.TenantContactEmail);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceFailureCodes.NotFound, opened.ErrorCode);
        Assert.Null(opened.Value);
    }

    // ----- One purpose cannot be opened as another -----

    /// <summary>Every purpose the product seals anything under. A purpose missing from this list is a purpose nothing pins.</summary>
    private static readonly string[] AllPurposes =
    [
        VoiceSecretPurposes.McpOAuthState,
        VoiceSecretPurposes.McpAccessToken,
        VoiceSecretPurposes.McpRefreshToken,
        VoiceSecretPurposes.TwilioAuthToken,
        VoiceSecretPurposes.KeyCheck,
        VoiceSecretPurposes.StorageAccessKeyId,
        VoiceSecretPurposes.StorageSecretAccessKey,
        VoiceSecretPurposes.TenantContactEmail,
        VoiceSecretPurposes.TenantContactPhone,
        VoiceSecretPurposes.ConfigTransferTargetNumber,
        VoiceSecretPurposes.ConfigMessageDestinationEmail,
    ];

    public static TheoryData<string> EveryPurpose()
    {
        var data = new TheoryData<string>();
        foreach (var purpose in AllPurposes) data.Add(purpose);
        return data;
    }

    /// <summary>
    /// The defect this closes, stated as a test: a bucket access key sealed under <c>McpAccessToken</c> opened as a Google access
    /// token, and the reverse. Every purpose is now distinct from every other, so a ciphertext from one column cannot be read as a
    /// value of another kind even with the right key.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPurpose))]
    public void A_value_sealed_for_one_purpose_opens_under_that_purpose_and_no_other(string purpose)
    {
        var key = NewKey();
        var protector = WithKey(key);
        const string Secret = "AKIAIOSFODNN7EXAMPLE";

        var sealedValue = protector.Protect(Secret, purpose);
        Assert.True(sealedValue.IsSuccess);
        Assert.Equal(Secret, protector.Unprotect(sealedValue.Value!, purpose).Value);

        foreach (var other in AllPurposes.Where(p => !string.Equals(p, purpose, StringComparison.Ordinal)))
        {
            var wrong = protector.Unprotect(sealedValue.Value!, other);
            Assert.False(wrong.IsSuccess, $"{purpose} opened as {other}");
            Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, wrong.ErrorCode);
        }
    }

    /// <summary>Eleven purposes, eleven distinct strings. Two that matched would be one purpose wearing two names.</summary>
    [Fact]
    public void No_two_purposes_are_the_same_string()
    {
        Assert.Equal(11, AllPurposes.Length);
        Assert.Equal(AllPurposes.Length, AllPurposes.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(AllPurposes, string.IsNullOrWhiteSpace);
    }

    // ----- The format byte, which is what makes rotating the key possible -----

    [Fact]
    public void A_sealed_value_carries_its_format_as_its_first_byte()
    {
        var sealedValue = WithKey(NewKey()).Protect("a secret", VoiceSecretPurposes.TenantContactPhone).Value!;

        Assert.Equal(1, Convert.FromBase64String(sealedValue)[0]);
    }

    /// <summary>
    /// A format this build does not know is refused, not read at the offsets of the one it does know. Guessing would open the value at
    /// the wrong boundaries and report whatever came out - and a value written before the byte existed is exactly that case.
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)255)]
    public void A_sealed_value_in_an_unknown_format_is_refused_by_its_own_code(byte version)
    {
        var key = NewKey();
        var protector = WithKey(key);
        var bytes = Convert.FromBase64String(protector.Protect("a secret", VoiceSecretPurposes.TenantContactPhone).Value!);
        bytes[0] = version;

        var opened = protector.Unprotect(Convert.ToBase64String(bytes), VoiceSecretPurposes.TenantContactPhone);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.UnsupportedVersion, opened.ErrorCode);
        Assert.Null(opened.Value);
    }

    /// <summary>A value too short to hold a format byte, a nonce and a tag is not well formed, and says so before the key is read.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("AQ==")]
    [InlineData("not base64 at all")]
    public void A_sealed_value_that_is_too_short_or_not_base64_is_refused(string stored)
    {
        var opened = new VoiceCredentialProtector(() => null).Unprotect(stored, VoiceSecretPurposes.TenantContactPhone);

        Assert.False(opened.IsSuccess);
        Assert.Equal(VoiceCredentialProtector.InvalidSecret, opened.ErrorCode);
    }

    // ----- What the form sends, sealed -----

    /// <summary>
    /// A business types its number the way people write one, as the signup's own contact phone already allows, and what is stored is
    /// the dialable form. The business is not asked to know what E.164 is.
    /// </summary>
    [Theory]
    [InlineData("(561) 555-0137")]
    [InlineData("561-555-0137")]
    [InlineData("5615550137")]
    [InlineData("+15615550137")]
    public void A_transfer_number_written_any_usual_way_is_stored_as_one_dialable_number(string typed)
    {
        var key = NewKey();
        var protector = WithKey(key);

        var sealedAnswers = new ReceptionistConfigAnswers(TransferTargetNumber: typed).Seal(protector);

        Assert.True(sealedAnswers.IsSuccess, sealedAnswers.Error);
        Assert.Equal(
            "+15615550137",
            protector.Unprotect(sealedAnswers.Value!.TransferTargetNumberEncrypted!, VoiceSecretPurposes.ConfigTransferTargetNumber).Value);
    }

    /// <summary>A question not asked seals nothing, and a question answered "none" seals nothing either. Neither is an error.</summary>
    [Fact]
    public void An_unasked_or_cleared_personal_detail_seals_to_nothing()
    {
        var protector = WithKey(NewKey());

        var unasked = new ReceptionistConfigAnswers(BookingEnabled: true).Seal(protector);
        var cleared = new ReceptionistConfigAnswers(TransferTargetNumber: "", MessageDestinationEmail: "").Seal(protector);

        Assert.True(unasked.IsSuccess);
        Assert.Null(unasked.Value!.TransferTargetNumberEncrypted);
        Assert.Null(unasked.Value.MessageDestinationEmailEncrypted);

        Assert.True(cleared.IsSuccess);
        Assert.Null(cleared.Value!.TransferTargetNumberEncrypted);
        Assert.Null(cleared.Value.MessageDestinationEmailEncrypted);
    }

    /// <summary>
    /// A business that typed something that is not a number or not an address is refused, and the answers it gave alongside are not
    /// half-written: sealing is the whole step, and it fails before any merge or save.
    /// </summary>
    [Theory]
    [InlineData("call me", null)]
    [InlineData(null, "no-at-sign")]
    public void A_step_carrying_a_detail_that_is_not_one_is_refused_whole(string? phone, string? email)
    {
        var refused = new ReceptionistConfigAnswers(
            GreetingMessage: "Acme Plumbing.", TransferTargetNumber: phone, MessageDestinationEmail: email).Seal(WithKey(NewKey()));

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Null(refused.Value);
    }
}
