using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// The languages a receptionist may be set to, and the words it says to a caller in each.
///
/// <para>
/// A language is in this registry only when every sentence spoken to a caller exists in it. That is the whole point of the type, and
/// the reason it holds text rather than just a list of locale codes: two of those sentences are the consent disclosure, which
/// <see cref="ReceptionistConsent"/> records as reviewed wording under Fla. Stat. 934.03, and a disclosure is a legal act rather than
/// a string. Jeff, 2026-11-20, on the suggestion that the conversation could run in Spanish while the gate stayed English until the
/// wording was reviewed: <em>"Thats lame, and one could claim one didn't understand."</em> He is right - consent that was not
/// understood is not consent, and a disclosure spoken in a language the caller does not speak is worse than none, because it leaves a
/// record that looks like a disclosure and does not survive the caller saying they did not understand it.
/// </para>
///
/// <para>
/// So the rule this type enforces is: <strong>a language we offer is a language whose caller-facing wording has been reviewed in
/// it.</strong> Adding one is adding an entry here, with that wording. Until an entry exists, <see cref="Find"/> refuses the code and
/// the writer refuses the answer, which is why no machine translation can reach a caller: there is nowhere to put one.
/// </para>
///
/// <para>
/// Only <c>en-US</c> is here today, carrying the existing approved wording verbatim. That is not a gap to be filled by this code - it
/// is the honest state of the reviewed text, and the registry is the thing that keeps the two in step.
/// </para>
///
/// <para>
/// When Spanish is added it is <c>es-US</c> and not <c>es-ES</c>. Twilio offers both with an ElevenLabs voice and Deepgram recognition,
/// so it is a free choice, and the census decides it: of 69.98M Hispanic or Latino people (ACS 2025, Table B03001) 56.9% are of Mexican
/// origin and 1.5% are Spaniards, so Castilian - <em>vosotros</em> and <em>distincion</em> - is the one variety the US market does not
/// call for. Recorded here because this is the file someone opens to add it. See <c>plans/multilingual-receptionist.md</c>.
/// </para>
///
/// <para>
/// The locale is Twilio's. <c>en-US</c> is also ConversationRelay's own documented default for an unset <c>language</c> attribute, so
/// storing it changes nothing about what a call sounds like today and makes the value explicit instead of inherited.
/// </para>
/// </summary>
public static class ReceptionistLanguages
{
    /// <summary>What a receptionist says to a caller, in one language. Every member is spoken aloud; none is an instruction to a model.</summary>
    /// <param name="Code">The stored value, and Twilio's BCP-47 locale: it is both, so there is nothing to map and nothing to get wrong.</param>
    /// <param name="EnglishName">For an owner choosing in the portal, and for an operator reading a log.</param>
    /// <param name="Disclosure">Spoken as ConversationRelay's welcomeGreeting on a passive-disclosure call. Reviewed wording.</param>
    /// <param name="Consent">Spoken by <c>&lt;Say&gt;</c> inside <c>&lt;Gather&gt;</c> on a strict-keypress call. Reviewed wording.</param>
    /// <param name="Unanswered">Spoken when the call cannot be taken at all.</param>
    /// <param name="Failure">Sent over the relay when the model fails mid-call. Promises nothing this product cannot do.</param>
    public sealed record Language(
        string Code,
        string EnglishName,
        Func<string, string> Disclosure,
        Func<string, string> Consent,
        Func<string, string> Unanswered,
        string Failure);

    /// <summary>ConversationRelay's documented default for an unset <c>language</c> attribute, and this product's default.</summary>
    public const string Default = "en-US";

    private static readonly Language EnglishUnitedStates = new(
        Code: "en-US",
        EnglishName: "English (United States)",
        Disclosure: business =>
            $"Hi, you've reached {business}'s AI assistant. Calls are processed in real time and not recorded. How can I help you today?",
        Consent: business =>
            $"Thank you for calling {business}. You will be interacting with our AI receptionist. Your audio will be temporarily recorded "
            + "and processed to provide this service, but we do not retain call recordings or transcripts. To consent and continue, press 1. "
            + "To decline, press 2.",
        Unanswered: business =>
            $"Thank you for calling {business}. We can't take your call right now. Please call again later. Goodbye.",
        Failure:
            "I'm having trouble completing that request right now. Please leave your name, phone number, and a brief message after the "
            + "tone, and our team will call you right back.");

    private static readonly IReadOnlyDictionary<string, Language> ByCode =
        new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase) { [EnglishUnitedStates.Code] = EnglishUnitedStates };

    /// <summary>Every language a receptionist may be set to, in the order an owner should be offered them.</summary>
    public static IReadOnlyList<Language> All { get; } = [EnglishUnitedStates];

    /// <summary>The stored codes, for a caller that only needs to validate one.</summary>
    public static IReadOnlyList<string> Codes { get; } = [EnglishUnitedStates.Code];

    /// <summary>
    /// The words for ConversationRelay's own default language. Used only where no stored language has been read yet - a call refused
    /// before its config is fetched, which is the closing message for a tenant whose subscription has lapsed. It is a default for the
    /// absence of a config, never a substitute for a config that named a language this code could not find: that case is a failure.
    /// </summary>
    public static Language DefaultLanguage => EnglishUnitedStates;

    /// <summary>True when a language may be stored, which is the same as saying its caller-facing wording has been reviewed.</summary>
    public static bool IsOffered(string? code) => !string.IsNullOrWhiteSpace(code) && ByCode.ContainsKey(code);

    /// <summary>
    /// The words for a stored language. A code with no entry is a failure and never a fall back to English: a receptionist that
    /// silently spoke English to a business expecting Spanish would be the quiet substitution this project does not do, and the
    /// disclosure it spoke would be the one nobody understood.
    /// </summary>
    public static Result<Language> Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result<Language>.Failure("The receptionist has no language set.", VoiceFailureCodes.InvalidRequest);

        return ByCode.TryGetValue(code, out var language)
            ? Result<Language>.Success(language)
            : Result<Language>.Failure(
                $"This receptionist is set to a language ({code}) that it has no approved wording for.",
                VoiceFailureCodes.InvalidRequest);
    }
}
