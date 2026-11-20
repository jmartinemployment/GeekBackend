using System.Text.Json;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Calls;
using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The language a receptionist speaks, and the one rule that makes it safe: a language is offerable only when every sentence a caller
/// hears exists in it. Two of those sentences are the consent disclosure, which is reviewed wording rather than translatable text, so
/// these tests are about refusing a language we cannot speak - not about translation.
/// </summary>
public class ReceptionistLanguageTests
{
    private static VoiceReceptionistConfig Config(string language) => new(
        GreetingMessage: "Hello.",
        SystemPromptTemplate: "You are a receptionist.",
        BusinessHours: JsonDocument.Parse("{}").RootElement,
        QualifyingQuestions: JsonDocument.Parse("[]").RootElement,
        TransferMode: VoiceTransferModes.VoicemailOnly,
        TransferTargetNumberEncrypted: null,
        TransferCallerIdMode: VoiceTransferCallerIdModes.AssignedAiNumber,
        EmergencyKeywords: [],
        RecordingDisclosureEnabled: false,
        Language: language);

    [Fact]
    public void Every_offered_code_has_wording_and_every_worded_language_is_offered()
    {
        // The two lists live in different projects - the codes in GeekApplication so the repository can validate, the words in GeekAPI
        // because that is where a caller is spoken to. This test is the only thing keeping them identical, and it exists because a
        // comment promising two lists agree is worth exactly the test that checks it.
        Assert.Equal(
            VoiceLanguages.All.OrderBy(c => c, StringComparer.Ordinal),
            ReceptionistLanguages.Codes.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_offered_language_says_every_sentence_a_caller_hears()
    {
        Assert.NotEmpty(ReceptionistLanguages.All);
        foreach (var language in ReceptionistLanguages.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.Code));
            Assert.False(string.IsNullOrWhiteSpace(language.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(language.Disclosure("Joe's TV")));
            Assert.False(string.IsNullOrWhiteSpace(language.Consent("Joe's TV")));
            Assert.False(string.IsNullOrWhiteSpace(language.Unanswered("Joe's TV")));
            Assert.False(string.IsNullOrWhiteSpace(language.Failure));
        }
    }

    [Fact]
    public void Each_caller_facing_sentence_names_the_business()
    {
        foreach (var language in ReceptionistLanguages.All)
        {
            Assert.Contains("Joe's TV", language.Disclosure("Joe's TV"));
            Assert.Contains("Joe's TV", language.Consent("Joe's TV"));
            Assert.Contains("Joe's TV", language.Unanswered("Joe's TV"));
        }
    }

    [Fact]
    public void The_default_is_the_language_ConversationRelay_would_have_used_anyway()
    {
        // Twilio documents en-US as the default for an unset `language` attribute, so storing it changes nothing about an existing call.
        Assert.Equal("en-US", ReceptionistLanguages.Default);
        Assert.Equal("en-US", VoiceLanguages.Default);
        Assert.Equal("en-US", ReceptionistLanguages.DefaultLanguage.Code);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("fr-FR")]
    [InlineData("multi")]
    [InlineData("en-GB")]
    public void A_language_with_no_approved_wording_is_refused_and_never_falls_back_to_English(string code)
    {
        Assert.False(ReceptionistLanguages.IsOffered(code));

        var found = ReceptionistLanguages.Find(code);
        Assert.False(found.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, found.ErrorCode);
        Assert.Contains(code, found.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_language_is_a_failure_rather_than_a_default(string? code)
    {
        Assert.False(ReceptionistLanguages.IsOffered(code));
        Assert.False(ReceptionistLanguages.Find(code).IsSuccess);
    }

    [Fact]
    public void The_prompt_tells_the_model_which_language_to_conduct_the_call_in()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config("en-US"), "Hello, you've reached Joe's TV.");

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("Conduct this entire call in English (United States)", prompt.Value);
        Assert.Contains("whatever language the caller uses", prompt.Value);
    }

    [Fact]
    public void A_prompt_cannot_be_built_for_a_language_the_receptionist_cannot_speak()
    {
        // The failure is at the prompt, before the call is handed to the AI: a config that named an unspeakable language must not
        // produce a working call in some other language.
        var prompt = ReceptionistConsent.SystemPrompt(Config("es-ES"), "Hola.");

        Assert.False(prompt.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, prompt.ErrorCode);
        Assert.Contains("es-ES", prompt.Error);
    }

    [Fact]
    public void The_relay_sets_both_halves_of_the_language_rather_than_leaving_either_inherited()
    {
        var twiml = ReceptionistConsent
            .Connect("wss://example.test/relay?ticket=abc", "Hello.", ReceptionistLanguages.DefaultLanguage)
            .ToString();

        Assert.Contains("ttsLanguage=\"en-US\"", twiml);
        Assert.Contains("transcriptionLanguage=\"en-US\"", twiml);
    }

    [Fact]
    public void The_consent_gate_speaks_in_the_same_language_as_the_conversation()
    {
        // <Say> inside <Gather> is a different TTS path from ConversationRelay and carries its own language. Unset, it would ask for
        // consent in English and then converse in the stored language - a call disclosed in a language the caller may not have.
        var twiml = ReceptionistConsent
            .Ask("Joe's TV", new Uri("https://example.test/consent"), ReceptionistLanguages.DefaultLanguage)
            .ToString();

        Assert.Contains("language=\"en-US\"", twiml);
        Assert.Contains("Joe's TV", twiml);
    }

    [Fact]
    public void The_closing_message_carries_a_language_too()
    {
        var twiml = CallTwiml.Unanswered("Joe's TV", ReceptionistLanguages.DefaultLanguage).ToString();

        Assert.Contains("language=\"en-US\"", twiml);
        Assert.Contains("Joe's TV", twiml);
    }

    [Fact]
    public void The_approved_English_wording_is_unchanged_by_being_moved_into_the_registry()
    {
        // The two reviewed sentences, asserted verbatim. If a refactor edits them, this fails rather than a caller hearing new wording
        // that counsel never read.
        Assert.Equal(
            "Hi, you've reached Joe's TV's AI assistant. Calls are processed in real time and not recorded. How can I help you today?",
            ReceptionistConsent.DisclosureText("Joe's TV", ReceptionistLanguages.DefaultLanguage));

        Assert.Equal(
            "Thank you for calling Joe's TV. You will be interacting with our AI receptionist. Your audio will be temporarily recorded "
            + "and processed to provide this service, but we do not retain call recordings or transcripts. To consent and continue, press 1. "
            + "To decline, press 2.",
            ReceptionistConsent.ConsentText("Joe's TV", ReceptionistLanguages.DefaultLanguage));
    }
}
