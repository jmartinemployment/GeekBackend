using System.Text.RegularExpressions;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice;

/// <summary>
/// The four personal details Geek-Receptionist stores, sealed on their way to GeekRepository: the business's own contact address and
/// number, the number its receptionist transfers a caller to, and the address its end-of-call messages go to. Jeff, 2026-11-20:
/// *"The application is a SaaS application, the variables are client data, shouldn't they be encrypted?"*
///
/// <para>
/// <b>The shape rules live here because GeekRepository can no longer read the value.</b> They are not a second copy of a check that
/// stayed behind: <c>ValidateCreateTenant</c>'s address check and <c>ValidateConfig</c>'s E.164 and address checks had nothing left to
/// look at once the column held one base64 string, so the rule moved up to the last place that holds the number or the address in the
/// clear. What stays in GeekRepository is the rule that can still be enforced on ciphertext - that the value is there at all.
/// </para>
///
/// <para>
/// Sealing is where a bad value is caught, so a refusal is a refusal to store rather than a row stored wrong. A value that is not a
/// number or not an address is never encrypted, and a key that cannot encrypt is reported rather than worked around: there is no path
/// here that writes a personal detail in the clear.
/// </para>
/// </summary>
internal static partial class VoicePersonalDetails
{
    /// <summary>E.164, as GeekRepository used to check it before the column became ciphertext.</summary>
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();

    /// <summary>
    /// A number, sealed for one purpose. Anything that is not E.164 is refused: the dialler needs a number it can dial, and nothing
    /// below this point can tell whether what it holds is one.
    /// </summary>
    internal static Result<string> SealPhone(VoiceCredentialProtector protector, string? phone, string purpose, string problem) =>
        phone is not null && E164().IsMatch(phone)
            ? protector.Protect(phone, purpose)
            : Result<string>.Failure(problem, VoiceFailureCodes.InvalidRequest);

    /// <summary>
    /// An address, sealed for one purpose. The round-trip comparison is the check GeekRepository used: an address that
    /// <see cref="System.Net.Mail.MailAddress"/> accepts but rewrites (a display name, angle brackets, trailing text) is not the
    /// address somebody typed, and sending to it later would send somewhere else.
    /// </summary>
    internal static Result<string> SealEmail(VoiceCredentialProtector protector, string? email, string purpose, string problem) =>
        IsEmailAddress(email)
            ? protector.Protect(email!, purpose)
            : Result<string>.Failure(problem, VoiceFailureCodes.InvalidRequest);

    /// <summary>The sealed value opened, or the protector's own refusal. Never a blank string standing in for one.</summary>
    internal static Result<string> Open(VoiceCredentialProtector protector, string? sealedValue, string purpose) =>
        string.IsNullOrWhiteSpace(sealedValue)
            ? Result<string>.Failure("There is nothing stored to read.", VoiceFailureCodes.NotFound)
            : protector.Unprotect(sealedValue, purpose);

    internal static bool IsEmailAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && System.Net.Mail.MailAddress.TryCreate(value, out var address)
        && string.Equals(address.Address, value, StringComparison.Ordinal);
}
