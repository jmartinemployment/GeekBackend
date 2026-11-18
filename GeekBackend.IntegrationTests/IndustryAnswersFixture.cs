using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekBackend.IntegrationTests;

/// <summary>The five excluded-industry questions each answered no, as an accepted signup stores them with its tenant.</summary>
internal static class IndustryAnswersFixture
{
    public static readonly (string Code, string Question)[] Questions =
    [
        ("hipaa", "Is the business a HIPAA covered entity or business associate, such as a healthcare provider, health plan or a business that handles patient health information for one?"),
        ("securities_broker_dealer", "Is the business a securities broker-dealer?"),
        ("investment_adviser", "Is the business an investment adviser?"),
        ("commodity_futures_derivatives", "Is the business a commodity futures or derivatives business?"),
        ("medicare_tpmo", "Is the business a Third-Party Marketing Organization (TPMO) involved in Medicare Advantage or Part D marketing and sales?"),
    ];

    public static VoiceIndustryAnswerSet AllNo(DateTimeOffset at) => new(
        Questions.Select(q => new VoiceIndustryAnswer(q.Code, q.Question, false)).ToList(), at, "203.0.113.9", "US");
}

/// <summary>A refusal store that records what it was asked to keep, or answers with the failure a test gives it.</summary>
internal sealed class RecordingSignupAnswerStore(Func<Result<VoiceIndustryAnswersRecorded>>? answer = null) : IVoiceSignupAnswerStore
{
    public List<VoiceIndustryAnswerSet> Refusals { get; } = new();

    public Task<Result<VoiceIndustryAnswersRecorded>> RecordIndustryRefusalAsync(VoiceIndustryAnswerSet answers, CancellationToken ct = default)
    {
        Refusals.Add(answers);
        return Task.FromResult(answer is not null ? answer() : Result<VoiceIndustryAnswersRecorded>.Success(new VoiceIndustryAnswersRecorded(answers.Answers.Count)));
    }
}
