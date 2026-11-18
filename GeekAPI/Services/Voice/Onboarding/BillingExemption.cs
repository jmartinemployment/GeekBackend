using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// The one contact email whose signup skips PayPal (BILLING_EXEMPT_EMAIL): Geek @ Your Spot LLC's own account, so its calendar can be
/// connected the way a client's is (Jeff, 2026-11-18: skip the PayPal rule for his account; nothing is faked). Unset, nobody is exempt.
/// </summary>
public sealed record BillingExemptionOptions(string? ExemptEmail)
{
    public static BillingExemptionOptions FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("BILLING_EXEMPT_EMAIL") is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null);
}

public sealed record BillingExemptionRequest(Guid? TenantId);

/// <summary>Whether the tenant was exempt; when it was, its status after activation.</summary>
public sealed record BillingExemptionOutcome(bool Exempt, string Status);

/// <summary>
/// Asked by the portal right after a business is accepted. A tenant whose contact email is the exempt one is activated with no
/// subscription and no telephony (<see cref="IVoiceBillingExemption"/>), and the portal goes straight to the owner's login. Every
/// other tenant is answered not exempt and goes on to PayPal; this never changes it.
/// </summary>
public sealed class BillingExemption(IVoiceStore voice, IVoiceBillingExemption exemption, BillingExemptionOptions options)
{
    public async Task<Result<BillingExemptionOutcome>> ApplyAsync(BillingExemptionRequest? request, CancellationToken ct = default)
    {
        if (request?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return Result<BillingExemptionOutcome>.Failure("A tenant is required.", VoiceFailureCodes.InvalidRequest);

        var tenant = await voice.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
            return Result<BillingExemptionOutcome>.Failure(tenant.Error ?? "The tenant could not be read.", tenant.ErrorCode);

        if (options.ExemptEmail is null || !string.Equals(tenant.Value!.ContactEmail.Trim(), options.ExemptEmail, StringComparison.OrdinalIgnoreCase))
            return Result<BillingExemptionOutcome>.Success(new BillingExemptionOutcome(false, tenant.Value!.Status));

        var activated = await exemption.ActivateWithoutBillingAsync(tenantId, ct).ConfigureAwait(false);
        return activated.IsSuccess
            ? Result<BillingExemptionOutcome>.Success(new BillingExemptionOutcome(true, activated.Value!.Status))
            : Result<BillingExemptionOutcome>.Failure(activated.Error ?? "The tenant could not be activated.", activated.ErrorCode);
    }
}
