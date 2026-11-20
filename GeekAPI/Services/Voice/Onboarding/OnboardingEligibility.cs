using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>Why the eligibility gate refused a signup. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class OnboardingFailureCodes
{
    public const string CountryNotSupported = "country_not_supported";
    public const string BusinessDescriptionRequired = "business_description_required";
    public const string IndustryNotAnswered = "industry_not_answered";
    public const string ContactPhoneNotUs = "contact_phone_not_us";
    public const string ContactEmailInvalid = "contact_email_invalid";
    public const string IndustryExcluded = "industry_excluded";
    public const string AttestationRequired = "attestation_required";
    public const string VisitorUnverified = "visitor_unverified";
    public const string VisitorCountryNotSupported = "visitor_country_not_supported";
}

/// <summary>
/// What the first step of /signup sends (docs/Geek-Receptionist.md section 5, item 1). Every field is nullable so a field that
/// was left out is told apart from one that was sent empty. The shape of the contact details is checked here, where they are still in
/// the clear: they are sealed before the tenant is stored, so GeekRepository can only check that they were sent at all.
/// </summary>
public sealed record ValidateEligibilityRequest(
    string? BusinessName,
    string? BusinessDescription,
    string? CountryCode,
    string? StateCode,
    string? ContactEmail,
    string? ContactPhone,
    bool? ComplianceAttested,
    IReadOnlyList<IndustryAnswerInput>? IndustryAnswers);

/// <summary>One excluded-industry question as the signup form sends it: the industry's code, the words it showed, and the answer.</summary>
public sealed record IndustryAnswerInput(string? Industry, string? Question, bool? Answer);

/// <summary>What the form sends the moment a visitor answers yes to an excluded industry: that answer and the ones before it.</summary>
public sealed record IndustryRefusalRequest(IReadOnlyList<IndustryAnswerInput>? Answers);

/// <summary>The tenant that now exists and is waiting for payment.</summary>
public sealed record EligibilityAccepted(Guid TenantId, string Status);

/// <summary>
/// The self-service onboarding gate. The three rules run in the order the plan lists them (country, excluded industry,
/// attestation); the first that fails answers. The excluded industries are decided on the form's eight separate yes-or-no answers
/// (<see cref="ExcludedIndustries"/>) (Jeff, 2026-11-18, replacing the list of business codes). The professional services question
/// and its addendum were removed by Jeff the same day. An answer that was not sent, or whose question is not the one this gate holds,
/// is refused, never read as a no. Every answer is stored and kept forever: a yes at once, with no tenant, and the eight noes with
/// the tenant in one write. The country rule has two parts: the country the form declares, and the country of
/// the visitor's own address (<see cref="VisitorOrigin"/>). A visitor whose address or country is missing is refused as unverified,
/// never let through. A signup that passes all three becomes a tenant in status pending_payment, the only
/// place the attestation can be stored. Nothing is created for a signup that fails, and a store that
/// cannot be reached is a failure, never an accepted signup.
/// </summary>
public sealed class OnboardingEligibility(
    IVoiceStore voice, IVoiceSignupAnswerStore signupAnswers, TimeProvider clock, VoiceCredentialProtector protector)
{
    public async Task<Result<EligibilityAccepted>> ValidateAsync(ValidateEligibilityRequest request, VisitorOrigin visitor, CancellationToken ct = default)
    {
        if (!string.Equals(request.CountryCode, "US", StringComparison.Ordinal))
            return Refused(OnboardingFailureCodes.CountryNotSupported, "Only US businesses can sign up.");

        if (string.IsNullOrWhiteSpace(visitor.IpAddress) || string.IsNullOrWhiteSpace(visitor.CountryCode))
            return Refused(OnboardingFailureCodes.VisitorUnverified, "The visitor's location could not be verified.");
        if (!string.Equals(visitor.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
            return Refused(OnboardingFailureCodes.VisitorCountryNotSupported, "Signup is open only from the United States.");

        var now = clock.GetUtcNow();
        var answers = ExcludedIndustries.Read(request.IndustryAnswers);
        if (answers is null || answers.Count != ExcludedIndustries.All.Count)
            return Refused(OnboardingFailureCodes.IndustryNotAnswered, "Every question about the business's industry must be answered.");

        var answerSet = new VoiceIndustryAnswerSet(answers, now, visitor.IpAddress, visitor.CountryCode);
        if (answers.Any(a => a.Answer))
        {
            // A yes is kept as well as refused. If it cannot be stored the visitor is still refused, with the store's own failure.
            var recorded = await signupAnswers.RecordIndustryRefusalAsync(answerSet, ct).ConfigureAwait(false);
            return recorded.IsSuccess
                ? Refused(OnboardingFailureCodes.IndustryExcluded, ExcludedIndustries.Refusal)
                : Result<EligibilityAccepted>.Failure(recorded.Error ?? "The answers could not be stored.", recorded.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }

        if (request.ComplianceAttested != true)
            return Refused(OnboardingFailureCodes.AttestationRequired, "The compliance attestation must be accepted.");

        if (string.IsNullOrWhiteSpace(request.BusinessDescription))
            return Refused(OnboardingFailureCodes.BusinessDescriptionRequired, "Say what the business does.");

        var phone = UsPhone.ToE164(request.ContactPhone);
        if (phone is null)
            return Refused(OnboardingFailureCodes.ContactPhoneNotUs, "Enter the business's US phone number, ten digits, such as (561) 555-0101.");

        if (!VoicePersonalDetails.IsEmailAddress(request.ContactEmail?.Trim()))
            return Refused(OnboardingFailureCodes.ContactEmailInvalid, "Enter the business's email address.");

        // The business's own address and number are sealed here, the last place they are held in the clear, so the tenant row and
        // every internal call about it carry ciphertext. A key that cannot seal refuses the signup rather than storing them bare.
        var sealedEmail = VoicePersonalDetails.SealEmail(
            protector, request.ContactEmail!.Trim(), VoiceSecretPurposes.TenantContactEmail, "Enter the business's email address.");
        if (!sealedEmail.IsSuccess)
            return Result<EligibilityAccepted>.Failure(sealedEmail.Error!, sealedEmail.ErrorCode!);

        var sealedPhone = VoicePersonalDetails.SealPhone(
            protector, phone, VoiceSecretPurposes.TenantContactPhone, "Enter the business's US phone number.");
        if (!sealedPhone.IsSuccess)
            return Result<EligibilityAccepted>.Failure(sealedPhone.Error!, sealedPhone.ErrorCode!);

        var created = await voice.CreateTenantAsync(new CreateVoiceTenantCommand(
            request.BusinessName ?? string.Empty,
            request.BusinessDescription.Trim(),
            now,
            request.CountryCode!,
            request.StateCode ?? string.Empty,
            sealedEmail.Value!,
            sealedPhone.Value!)
        {
            IndustryAnswers = answerSet,
        }, ct).ConfigureAwait(false);

        return created.IsSuccess
            ? Result<EligibilityAccepted>.Success(new EligibilityAccepted(created.Value!.Id, created.Value.Status))
            : Result<EligibilityAccepted>.Failure(created.Error ?? "The tenant could not be created.", created.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    private static Result<EligibilityAccepted> Refused(string code, string message) => Result<EligibilityAccepted>.Failure(message, code);
}

/// <summary>
/// The moment a visitor answers yes to an excluded industry on /signup: the yes, and the noes given before it, are stored with no
/// tenant and kept forever (docs/Geek-Receptionist.md section 5, item 1). The visitor's address and country must be verified, as
/// for a signup. Nothing is stored for a request with no yes in it, or with an answer this gate does not recognise.
/// </summary>
public sealed class IndustryRefusals(IVoiceSignupAnswerStore signupAnswers, TimeProvider clock)
{
    public async Task<Result<VoiceIndustryAnswersRecorded>> RecordAsync(IndustryRefusalRequest? request, VisitorOrigin visitor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(visitor.IpAddress) || string.IsNullOrWhiteSpace(visitor.CountryCode))
            return Result<VoiceIndustryAnswersRecorded>.Failure("The visitor's location could not be verified.", OnboardingFailureCodes.VisitorUnverified);

        var answers = ExcludedIndustries.Read(request?.Answers);
        if (answers is null || !answers.Any(a => a.Answer))
            return Result<VoiceIndustryAnswersRecorded>.Failure("A refusal carries a yes to one of the excluded industries.", OnboardingFailureCodes.IndustryNotAnswered);

        return await signupAnswers.RecordIndustryRefusalAsync(
            new VoiceIndustryAnswerSet(answers, clock.GetUtcNow(), visitor.IpAddress, visitor.CountryCode), ct).ConfigureAwait(false);
    }
}

/// <summary>One industry that cannot sign up: its code, and the question the signup form asks about it, word for word.</summary>
public sealed record ExcludedIndustry(string Code, string Question);

/// <summary>
/// The industries that cannot sign up (docs/Geek-Receptionist.md section 5, item 1), exactly these five, set by Jeff on 2026-11-18:
/// HIPAA covered entities and business associates (replacing the healthcare question), securities broker-dealers, investment
/// advisers, commodity futures and derivatives businesses, and Third-Party Marketing Organizations (TPMOs) involved in Medicare
/// Advantage or Part D marketing and sales (replacing the Medicare sales question). Veterinary and telemarketing businesses are no
/// longer excluded. HIPAA is asked as four questions, one each for a healthcare provider, a health plan, a healthcare
/// clearinghouse and a business associate (Jeff, 2026-11-18), so the five industries are eight questions; the single `hipaa`
/// question is retired. Answers already stored under the old codes are kept forever and are not rewritten; this gate accepts only
/// these eight. The form asks about each one separately (Jeff, the same day: "more enforceable to ask each separately"). The portal holds the same codes and questions in lib/signup/excluded.ts; the two
/// change together, and an answer whose question is not the one here is refused, so what is stored is what was shown.
/// </summary>
public static class ExcludedIndustries
{
    public static readonly IReadOnlyList<ExcludedIndustry> All =
    [
        new("hipaa_healthcare_provider", "Is the business a healthcare provider?"),
        new("hipaa_health_plan", "Is the business a health plan?"),
        new("hipaa_clearinghouse", "Is the business a healthcare clearinghouse?"),
        new("hipaa_business_associate", "Does the business handle patient health information for a healthcare provider or health plan?"),
        new("securities_broker_dealer", "Is the business a securities broker-dealer?"),
        new("investment_adviser", "Is the business an investment adviser?"),
        new("commodity_futures_derivatives", "Is the business a commodity futures or derivatives business?"),
        new("medicare_tpmo", "Is the business a Third-Party Marketing Organization (TPMO) involved in Medicare Advantage or Part D marketing and sales?"),
    ];

    public const string Refusal =
        "HIPAA covered entities and business associates, securities broker-dealers, investment advisers, commodity futures or derivatives businesses, and Third-Party Marketing Organizations (TPMOs) involved in Medicare Advantage or Part D marketing and sales cannot use the receptionist.";

    /// <summary>
    /// The answers as sent, in this list's order, or null when any one of them is not an answer to a question held here: an unknown
    /// or repeated industry, words that differ from the question, or no yes or no. Fewer than all of them is still a reading; the
    /// caller decides whether every question had to be answered.
    /// </summary>
    public static IReadOnlyList<VoiceIndustryAnswer>? Read(IReadOnlyList<IndustryAnswerInput>? sent)
    {
        if (sent is not { Count: > 0 }) return null;
        var read = new Dictionary<string, VoiceIndustryAnswer>(StringComparer.Ordinal);
        foreach (var input in sent)
        {
            if (input is null || input.Answer is null) return null;
            var industry = All.FirstOrDefault(i => string.Equals(i.Code, input.Industry, StringComparison.Ordinal));
            if (industry is null || !string.Equals(industry.Question, input.Question, StringComparison.Ordinal)) return null;
            if (!read.TryAdd(industry.Code, new VoiceIndustryAnswer(industry.Code, industry.Question, input.Answer.Value))) return null;
        }

        return All.Where(i => read.ContainsKey(i.Code)).Select(i => read[i.Code]).ToList();
    }
}
