using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// The excluded-industry answers of a visitor who was refused at /signup, so no tenant exists to hold them (docs/Geek-Receptionist.md
/// section 5, item 1). A business that was accepted has its answers written with its tenant, by <see cref="IVoiceStore.CreateTenantAsync"/>.
/// Rows are only ever added: the store has no method that changes or removes one.
/// </summary>
public interface IVoiceSignupAnswerStore
{
    Task<Result<VoiceIndustryAnswersRecorded>> RecordIndustryRefusalAsync(VoiceIndustryAnswerSet answers, CancellationToken ct = default);

    /// <summary>
    /// A business's acceptance of the Terms, written once and kept forever. A second call for the same tenant is not a second row: it
    /// answers <see cref="VoiceFailureCodes.Conflict"/> and the first stays as it was.
    /// </summary>
    Task<Result<VoiceTermsAcceptanceState>> RecordTermsAcceptanceAsync(VoiceTermsAcceptance acceptance, CancellationToken ct = default);

    /// <summary>Whether the tenant has accepted the Terms. A tenant that does not exist is <see cref="VoiceFailureCodes.NotFound"/>, never "no".</summary>
    Task<Result<VoiceTermsAcceptanceState>> GetTermsAcceptanceAsync(Guid tenantId, CancellationToken ct = default);
}
