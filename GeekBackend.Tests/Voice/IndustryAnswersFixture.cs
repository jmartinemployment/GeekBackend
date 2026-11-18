using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekBackend.Tests.Voice;

/// <summary>The seven excluded-industry questions each answered no, as an accepted signup stores them with its tenant.</summary>
internal static class IndustryAnswersFixture
{
    public static readonly (string Code, string Question)[] Questions =
    [
        ("healthcare", "Is the business a healthcare business?"),
        ("veterinary", "Is the business a veterinary business?"),
        ("securities_broker_dealer", "Is the business a securities broker-dealer?"),
        ("investment_adviser", "Is the business an investment adviser?"),
        ("commodity_futures_derivatives", "Is the business a commodity futures or derivatives business?"),
        ("medicare_sales", "Is the business a Medicare sales organization?"),
        ("telemarketing", "Is the business a telemarketing business?"),
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
