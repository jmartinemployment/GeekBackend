using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>Why the eligibility gate refused a signup. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class OnboardingFailureCodes
{
    public const string CountryNotSupported = "country_not_supported";
    public const string BusinessActivityUnknown = "business_activity_unknown";
    public const string IndustryExcluded = "industry_excluded";
    public const string IndustryAddendumNotAvailable = "industry_addendum_not_available";
    public const string AddendumRequired = "addendum_required";
    public const string AttestationRequired = "attestation_required";
    public const string VisitorUnverified = "visitor_unverified";
    public const string VisitorCountryNotSupported = "visitor_country_not_supported";
}

/// <summary>
/// Whether industry addendums are published, read once from FeatureFlags:EnableIndustryAddendums (Railway variable
/// FeatureFlags__EnableIndustryAddendums). Unset means false, the documented default. A value that is not true or false stops the
/// service from starting: a typo must not silently keep a blocked industry blocked, or open one that should stay closed.
/// </summary>
public sealed record OnboardingOptions(bool EnableIndustryAddendums)
{
    public const string FlagKey = "FeatureFlags:EnableIndustryAddendums";

    public static OnboardingOptions From(IConfiguration configuration)
    {
        var raw = configuration[FlagKey];
        if (string.IsNullOrWhiteSpace(raw)) return new OnboardingOptions(false);
        if (!bool.TryParse(raw.Trim(), out var enabled))
            throw new InvalidOperationException($"{FlagKey} must be true or false; it is \"{raw}\".");
        return new OnboardingOptions(enabled);
    }
}

/// <summary>
/// What the first step of /signup sends (docs/Geek-Receptionist.md section 5, item 1). Every field is nullable so a field that
/// was left out is told apart from one that was sent empty; the shape of the contact details is checked by GeekRepository, once,
/// when the tenant is stored.
/// </summary>
public sealed record ValidateEligibilityRequest(
    string? BusinessName,
    string? BusinessActivityCode,
    string? CountryCode,
    string? StateCode,
    string? County,
    string? ContactEmail,
    string? ContactPhone,
    bool? ComplianceAttested,
    bool? AddendumSigned);

/// <summary>The tenant that now exists and is waiting for payment.</summary>
public sealed record EligibilityAccepted(Guid TenantId, string Status);

/// <summary>
/// The self-service onboarding gate. The four rules run in the order the plan lists them (country, healthcare, addendum-pending,
/// attestation); the first that fails answers. Healthcare and addendum-pending are decided on the IRS business activity code the
/// signup chose (<see cref="BusinessActivities"/>), and a code the chart does not have is refused before them. The country rule has two parts: the country the form declares, and the country of
/// the visitor's own address (<see cref="VisitorOrigin"/>). A visitor whose address or country is missing is refused as unverified,
/// never let through. A signup that passes all four becomes a tenant in status pending_payment, the only
/// place the attestation and the addendum record can be stored. Nothing is created for a signup that fails, and a store that
/// cannot be reached is a failure, never an accepted signup.
/// </summary>
public sealed class OnboardingEligibility(IVoiceStore voice, OnboardingOptions options, TimeProvider clock)
{
    public async Task<Result<EligibilityAccepted>> ValidateAsync(ValidateEligibilityRequest request, VisitorOrigin visitor, CancellationToken ct = default)
    {
        if (!string.Equals(request.CountryCode, "US", StringComparison.Ordinal))
            return Refused(OnboardingFailureCodes.CountryNotSupported, "Only US businesses can sign up.");

        if (string.IsNullOrWhiteSpace(visitor.IpAddress) || string.IsNullOrWhiteSpace(visitor.CountryCode))
            return Refused(OnboardingFailureCodes.VisitorUnverified, "The visitor's location could not be verified.");
        if (!string.Equals(visitor.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
            return Refused(OnboardingFailureCodes.VisitorCountryNotSupported, "Signup is open only from the United States.");

        var activity = BusinessActivities.Find(request.BusinessActivityCode);
        if (activity is null)
            return Refused(OnboardingFailureCodes.BusinessActivityUnknown, "Choose what the business does from the list of IRS business activity codes.");

        if (BusinessActivities.IsHealthcare(activity))
            return Refused(OnboardingFailureCodes.IndustryExcluded, "Healthcare and veterinary businesses cannot use the receptionist.");

        var requiresAddendum = BusinessActivities.RequiresAddendum(activity);
        if (requiresAddendum && !options.EnableIndustryAddendums)
            return Refused(OnboardingFailureCodes.IndustryAddendumNotAvailable, "The industry addendum for this business is not available yet.");

        if (request.ComplianceAttested != true)
            return Refused(OnboardingFailureCodes.AttestationRequired, "The compliance attestation must be accepted.");

        DateTimeOffset? addendumSignedAt = null;
        string? addendumAddress = null;
        var now = clock.GetUtcNow();
        if (requiresAddendum)
        {
            if (request.AddendumSigned != true)
                return Refused(OnboardingFailureCodes.AddendumRequired, "This industry requires a signed addendum.");
            addendumSignedAt = now;
            addendumAddress = visitor.IpAddress;
        }

        var created = await voice.CreateTenantAsync(new CreateVoiceTenantCommand(
            request.BusinessName ?? string.Empty,
            activity.Name,
            activity.Code,
            requiresAddendum,
            addendumSignedAt,
            addendumAddress,
            now,
            request.CountryCode!,
            request.StateCode ?? string.Empty,
            request.County,
            request.ContactEmail ?? string.Empty,
            request.ContactPhone ?? string.Empty), ct).ConfigureAwait(false);

        return created.IsSuccess
            ? Result<EligibilityAccepted>.Success(new EligibilityAccepted(created.Value!.Id, created.Value.Status))
            : Result<EligibilityAccepted>.Failure(created.Error ?? "The tenant could not be created.", created.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    private static Result<EligibilityAccepted> Refused(string code, string message) => Result<EligibilityAccepted>.Failure(message, code);
}
