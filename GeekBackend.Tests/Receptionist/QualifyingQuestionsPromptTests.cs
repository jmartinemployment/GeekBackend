using System.Text.Json;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The questions a business asks every caller, as the receptionist is told them. The signup form collects them one to a line, so a
/// plain list is the shape a business's answer actually arrives in - and until this was read, a plain list was stored and validated
/// happily and then refused on every call the business took. A setting accepted at signup and rejected at the telephone is worse than
/// one refused outright: the owner is never told.
/// </summary>
public sealed class QualifyingQuestionsPromptTests
{
    private static VoiceReceptionistConfig Config(string questionsJson) =>
        ReceptionistDefaults.ConfigFor(ReceptionistDefaults.ScriptFor("Dag Plumbing", "FL", "plumbing").Value!)
            with { QualifyingQuestions = JsonDocument.Parse(questionsJson).RootElement };

    [Fact]
    public void The_questions_a_business_typed_one_to_a_line_reach_the_call()
    {
        var prompt = ReceptionistConsent.SystemPrompt(
            Config("""["What is the address?", "Is anyone there now?", "Is the water off?"]"""));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("Ask these of every caller", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("- What is the address?", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("- Is anyone there now?", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("- Is the water off?", prompt.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_business_that_asked_for_no_questions_is_told_nothing_about_questions()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config("[]"));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.DoesNotContain("Qualifying questions", prompt.Value!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The area shape Geek @ Your Spot LLC's own receptionist uses is unchanged by the list being read. Both shapes are real answers
    /// and neither takes the other's branch.
    /// </summary>
    [Fact]
    public void The_area_shape_still_reads_as_areas()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config("""
            {
              "openingQuestion": "What brought you in today?",
              "areas": [{ "name": "Marketing", "questions": ["What are you running now?"] }]
            }
            """));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("Opening question: What brought you in today?", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Marketing: What are you running now?", prompt.Value!, StringComparison.Ordinal);
        Assert.DoesNotContain("Ask these of every caller", prompt.Value!, StringComparison.Ordinal);
    }

    /// <summary>A list holding something that is not a question is refused, not skipped and not partly read.</summary>
    [Theory]
    [InlineData("""["What is the address?", 7]""")]
    [InlineData("""["What is the address?", ""]""")]
    [InlineData("""["What is the address?", "   "]""")]
    [InlineData("""[{ "question": "What is the address?" }]""")]
    public void A_list_that_is_not_a_list_of_questions_is_refused(string json)
    {
        var refused = ReceptionistConsent.SystemPrompt(Config(json));

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
    }
}
