using GeekAPI.Services.Voice.Billing;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>The portal asking PayPal for a subscription or a saved payment method for a signup: the tenant, and where PayPal sends the visitor back.</summary>
public sealed record StartSignupPaymentRequest(Guid? TenantId, string? TierCode, string? ReturnUrl, string? CancelUrl);

/// <summary>The portal handing back the setup token the visitor approved in PayPal.</summary>
public sealed record CompleteSignupVaultRequest(Guid? TenantId, string? SetupTokenId);

/// <summary>The payment token overage charges will use; the portal sends it to activate-subscription.</summary>
public sealed record SignupVaultSaved(string PaypalVaultTokenId);

/// <summary>
/// The PayPal half of /signup's second step (docs/Geek-Receptionist.md section 5, item 2): only for a tenant that passed the
/// eligibility gate and is still waiting for payment, so nothing can be started for a tenant that is active, suspended, cancelled or
/// unknown. The visitor approves both in PayPal; activation then checks both again with PayPal before anything is bought.
/// </summary>
public sealed class SignupPayments(IVoiceStore store, IVoiceSignupAnswerStore signupAnswers, PayPalSignup paypal)
{
    public async Task<Result<PayPalApproval>> StartSubscriptionAsync(StartSignupPaymentRequest? request, CancellationToken ct = default)
    {
        var tier = VoiceTierSchedule.Find(request?.TierCode);
        if (tier is null)
            return Result<PayPalApproval>.Failure($"The tier must be one of: {string.Join(", ", VoiceTierSchedule.All.Select(t => t.Code))}.", BillingFailureCodes.TierUnknown);

        var checkedRequest = await CheckAsync(request, ct).ConfigureAwait(false);
        if (!checkedRequest.IsSuccess) return Result<PayPalApproval>.Failure(checkedRequest.Error!, checkedRequest.ErrorCode!);
        var (tenantId, returnUrl, cancelUrl) = checkedRequest.Value;

        // The Terms come before the money: nothing is started with PayPal for a business that has not accepted them.
        var accepted = await signupAnswers.GetTermsAcceptanceAsync(tenantId, ct).ConfigureAwait(false);
        if (!accepted.IsSuccess) return Result<PayPalApproval>.Failure(accepted.Error!, accepted.ErrorCode!);
        if (!accepted.Value!.Accepted)
            return Result<PayPalApproval>.Failure("The Terms must be accepted before a plan is chosen.", OnboardingFailureCodes.TermsRequired);

        // A bucket connected during signup is a Standard-and-above ability, and the plan is chosen after it, so the check is here: a tier
        // without object storage is refused for a business that connected a bucket, and nothing is quietly saved and never used.
        var bucket = await store.GetStorageConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (bucket.IsSuccess && !VoiceSubscriptionTiers.WithObjectStorage.Contains(tier.Code, StringComparer.Ordinal))
            return Result<PayPalApproval>.Failure("Writing call records to your own bucket is part of Standard and above. Choose Standard or above, or use Google Drive, which is on every plan.", OnboardingFailureCodes.TierRequired);
        if (!bucket.IsSuccess && bucket.ErrorCode != VoiceFailureCodes.NotFound)
            return Result<PayPalApproval>.Failure(bucket.Error ?? "The storage connection could not be read.", bucket.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        return await paypal.StartSubscriptionAsync(tenantId, tier, returnUrl, cancelUrl, ct).ConfigureAwait(false);
    }

    public async Task<Result<PayPalApproval>> StartVaultAsync(StartSignupPaymentRequest? request, CancellationToken ct = default)
    {
        var checkedRequest = await CheckAsync(request, ct).ConfigureAwait(false);
        if (!checkedRequest.IsSuccess) return Result<PayPalApproval>.Failure(checkedRequest.Error!, checkedRequest.ErrorCode!);
        var (_, returnUrl, cancelUrl) = checkedRequest.Value;
        return await paypal.StartVaultAsync(returnUrl, cancelUrl, ct).ConfigureAwait(false);
    }

    public async Task<Result<SignupVaultSaved>> CompleteVaultAsync(CompleteSignupVaultRequest? request, CancellationToken ct = default)
    {
        var setupToken = request?.SetupTokenId?.Trim();
        if (string.IsNullOrEmpty(setupToken) || setupToken.Length > 64 || !setupToken.All(char.IsAsciiLetterOrDigit))
            return Result<SignupVaultSaved>.Failure("PayPal's setup token is required.", VoiceFailureCodes.InvalidRequest);

        var pending = await PendingAsync(request?.TenantId, ct).ConfigureAwait(false);
        if (!pending.IsSuccess) return Result<SignupVaultSaved>.Failure(pending.Error!, pending.ErrorCode!);

        var saved = await paypal.CompleteVaultAsync(setupToken, ct).ConfigureAwait(false);
        return saved.IsSuccess
            ? Result<SignupVaultSaved>.Success(new SignupVaultSaved(saved.Value!))
            : Result<SignupVaultSaved>.Failure(saved.Error!, saved.ErrorCode!);
    }

    private async Task<Result<(Guid TenantId, Uri ReturnUrl, Uri CancelUrl)>> CheckAsync(StartSignupPaymentRequest? request, CancellationToken ct)
    {
        var returnUrl = ReturnAddress.Parse(request?.ReturnUrl);
        var cancelUrl = ReturnAddress.Parse(request?.CancelUrl);
        if (returnUrl is null || cancelUrl is null)
            return Result<(Guid, Uri, Uri)>.Failure("Both return addresses must be https addresses (http only for localhost).", VoiceFailureCodes.InvalidRequest);

        var pending = await PendingAsync(request?.TenantId, ct).ConfigureAwait(false);
        return pending.IsSuccess
            ? Result<(Guid, Uri, Uri)>.Success((pending.Value, returnUrl, cancelUrl))
            : Result<(Guid, Uri, Uri)>.Failure(pending.Error!, pending.ErrorCode!);
    }

    private async Task<Result<Guid>> PendingAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is not { } id || id == Guid.Empty) return Result<Guid>.Failure("A tenant is required.", VoiceFailureCodes.InvalidRequest);

        var tenant = await store.GetTenantAsync(id, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess) return Result<Guid>.Failure(tenant.Error!, tenant.ErrorCode!);
        if (tenant.Value!.Status != VoiceTenantStatuses.PendingPayment)
            return Result<Guid>.Failure("This signup is already finished.", ActivationFailureCodes.TenantNotPending);
        return Result<Guid>.Success(id);
    }
}
