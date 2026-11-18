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
}
