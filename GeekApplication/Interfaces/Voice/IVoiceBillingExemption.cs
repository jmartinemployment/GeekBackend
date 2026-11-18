using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// Activating a tenant that is exempt from billing: Geek @ Your Spot LLC's own account (Jeff, 2026-11-18: skip the PayPal rule for
/// his account, nothing faked). The tenant becomes active with no subscription row and no telephony row, so the billing jobs never
/// see it and no cancel can release a Twilio account for it. Who is exempt is decided by GeekAPI, never here.
/// </summary>
public interface IVoiceBillingExemption
{
    Task<Result<VoiceTenant>> ActivateWithoutBillingAsync(Guid tenantId, CancellationToken ct = default);
}
