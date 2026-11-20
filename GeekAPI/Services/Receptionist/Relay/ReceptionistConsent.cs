using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using HttpMethod = Twilio.Http.HttpMethod;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// The consent gate every call to the AI passes through, in the plan's two modes (plans/client-and-app-plan.md, "Upfront consent gate
/// (Two-Mode Tenant Setting)"), and the TwiML that hands the caller to ConversationRelay.
///
/// Passive disclosure is the plan's default: <see cref="DisclosureText"/> is spoken by ConversationRelay as the call connects, before
/// the AI engages, and the call is not interrupted. Strict keypress is the option for an owner who prefers active opt-in, and is
/// <see cref="ConsentText"/> with a key. Either way nothing is recorded: the consent TwiML takes keys only, no Record and no speech
/// input, and the plan notes the final wording is subject to Florida counsel's review under Fla. Stat. 934.03.
/// </summary>
public static class ReceptionistConsent
{
    public const string ConsentDigit = "1";

    /// <summary>
    /// The plan's passive disclosure, word for word, with the business name, in the receptionist's language. It ends by asking what the
    /// caller wants, so it is both the disclosure and the greeting: ConversationRelay speaks it and the AI takes the call from there
    /// without greeting twice.
    /// </summary>
    public static string DisclosureText(string business, ReceptionistLanguages.Language language) => language.Disclosure(business);

    /// <summary>Jeff's approved wording, word for word, with the business name, in the receptionist's language.</summary>
    public static string ConsentText(string business, ReceptionistLanguages.Language language) => language.Consent(business);

    /// <summary>
    /// Asks for consent and posts the key to <paramref name="action"/>. With no key, Twilio does not post and goes on to the next verb:
    /// the closing message and a hang-up.
    /// </summary>
    public static VoiceResponse Ask(string business, Uri action, ReceptionistLanguages.Language language)
    {
        var response = new VoiceResponse();
        var gather = new Gather(
            input: [Gather.InputEnum.Dtmf],
            numDigits: 1,
            action: action,
            method: HttpMethod.Post);
        // <Say> is a different TTS path from ConversationRelay and carries its own language: leaving it unset would ask for consent in
        // English and then converse in the stored language, which is the half-translated call this registry exists to prevent.
        gather.Say(ConsentText(business, language), language: language.Code);
        response.Append(gather);
        response.Say(CallTwiml.UnansweredText(business, language), language: language.Code);
        response.Hangup();
        return response;
    }

    /// <summary>True only for a 1. A 2, nothing, or any other key declines.</summary>
    public static bool Consented(string? digits) => string.Equals(digits, ConsentDigit, StringComparison.Ordinal);

    /// <summary>
    /// Hands the call to ConversationRelay at <paramref name="relayUrl"/>, which speaks <paramref name="greeting"/> first. Only the
    /// documented url and welcomeGreeting are set: no intelligenceService, the one attribute Twilio documents for persisting transcripts.
    /// </summary>
    public static VoiceResponse Connect(string relayUrl, string greeting, ReceptionistLanguages.Language language)
    {
        var response = new VoiceResponse();
        var connect = new Connect();
        // Both halves are set rather than the single `language` attribute they would both inherit from, because Twilio documents them as
        // independent and overridable: naming each one leaves nothing to infer about which language is recognised and which is spoken.
        // Unset, ConversationRelay documents its default as en-US, so this makes explicit what every call has been running on inherited.
        connect.Append(new ConversationRelay(
            url: relayUrl,
            welcomeGreeting: greeting,
            ttsLanguage: language.Code,
            transcriptionLanguage: language.Code));
        response.Append(connect);
        return response;
    }

    /// <summary>The wss:// address of a relay path on PUBLIC_BASE_URL's host, with its one-time ticket.</summary>
    public static string RelayUrl(string publicBaseUrl, string path, string ticket) =>
        $"wss://{new Uri(publicBaseUrl).Authority}{path}?ticket={Uri.EscapeDataString(ticket)}";

    /// <summary>
    /// The system prompt a call is answered with: the stored prompt, the greeting the caller has already heard, and the qualifying
    /// questions when there are any. A shape of qualifying questions this code does not read is a failure, not skipped.
    /// </summary>
    public static Result<string> SystemPrompt(VoiceReceptionistConfig config, string? spokenGreeting = null)
    {
        if (string.IsNullOrWhiteSpace(config.SystemPromptTemplate) || string.IsNullOrWhiteSpace(config.GreetingMessage))
            return Result<string>.Failure("The receptionist has no prompt or greeting.", VoiceFailureCodes.InvalidRequest);

        // What the caller actually heard, which in passive disclosure is the disclosure and not the stored greeting.
        var greeting = string.IsNullOrWhiteSpace(spokenGreeting) ? config.GreetingMessage : spokenGreeting;

        var prompt = new StringBuilder(config.SystemPromptTemplate.Trim());
        prompt.Append("\n\nThe caller has already heard this greeting: ").Append(greeting.Trim());

        // What the business is open, which it gave at signup and the receptionist could not see until now.
        var hours = BusinessHoursText(config.BusinessHours);
        if (!hours.IsSuccess) return Result<string>.Failure(hours.Error!, hours.ErrorCode!);
        prompt.Append(hours.Value!.Length == 0
            ? "\n\nThis business has not given its opening hours. If a caller asks when it is open, say you do not have the hours and offer to take a message. Never guess them."
            : "\n\nOpening hours, which are the only hours you may give a caller. Never guess beyond them:" + hours.Value);

        // The rule that makes a wrong answer impossible rather than unlikely, and it is deliberately not conditional on the business
        // having an indexed site. A receptionist that may be given a lookup tool, or may not, must behave the same either way: a prompt
        // that announced a tool the toolbox did not offer would be a claim the call could not honour, and the business's own information
        // is the only thing standing between a caller and an invented price. This is the same instruction the missing-hours branch gives,
        // widened to everything about the business.
        prompt.Append("\n\nNever state a fact about this business - a price, a service, an availability, a policy - that you have not been "
            + "given here or found with a tool. If you have not been given it, say you will take a message and have someone call back. "
            + "Guessing is worse than taking a message.");

        // The model is instructed in English and answers in the caller's language. This line is the only thing that makes that true, and
        // it is not conditional on the language being non-English: naming en-US explicitly costs nothing and means a prompt read by an
        // operator says which language the call runs in rather than leaving it to be inferred from the absence of an instruction.
        var language = ReceptionistLanguages.Find(config.Language);
        if (!language.IsSuccess) return Result<string>.Failure(language.Error!, language.ErrorCode!);
        prompt.Append("\n\nConduct this entire call in ").Append(language.Value!.EnglishName)
            .Append(". Every reply you speak must be in that language, whatever language the caller uses. Do not offer to switch languages.");

        var questions = config.QualifyingQuestions;
        if (questions.ValueKind == JsonValueKind.Array && questions.GetArrayLength() == 0)
            return Result<string>.Success(prompt.ToString());

        // A plain list is what a business answers on one line each, and it is asked of every caller. It is read here as well as the
        // area shape because the form writes the list: a shape the repository stores and this method refused would be a business whose
        // questions were accepted at signup and whose every call then failed.
        if (questions.ValueKind == JsonValueKind.Array)
        {
            if (!questions.EnumerateArray().All(q => q.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(q.GetString())))
                return Result<string>.Failure("The qualifying questions are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);

            prompt.Append("\n\nQualifying questions. Ask these of every caller, in this order, as the conversation allows:");
            foreach (var q in questions.EnumerateArray()) prompt.Append("\n- ").Append(q.GetString());
            return Result<string>.Success(prompt.ToString());
        }

        if (questions.ValueKind != JsonValueKind.Object
            || !questions.TryGetProperty("openingQuestion", out var opening) || opening.ValueKind != JsonValueKind.String
            || !questions.TryGetProperty("areas", out var areas) || areas.ValueKind != JsonValueKind.Array)
            return Result<string>.Failure("The qualifying questions are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);

        prompt.Append("\n\nQualifying questions. Ask the opening question, then the three questions of the area the caller chose.");
        prompt.Append("\nOpening question: ").Append(opening.GetString());
        foreach (var area in areas.EnumerateArray())
        {
            if (!area.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !area.TryGetProperty("questions", out var list) || list.ValueKind != JsonValueKind.Array)
                return Result<string>.Failure("The qualifying questions are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);
            prompt.Append('\n').Append(name.GetString()).Append(':');
            foreach (var q in list.EnumerateArray())
            {
                if (q.ValueKind != JsonValueKind.String)
                    return Result<string>.Failure("The qualifying questions are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);
                prompt.Append(' ').Append(q.GetString());
            }
        }

        return Result<string>.Success(prompt.ToString());
    }

    /// <summary>
    /// The business's opening hours as the receptionist is told them, or nothing when the business has given none. The shape is:
    ///
    /// <code>
    /// { "timezone": "America/New_York",
    ///   "days": [ { "day": "monday", "open": "08:00", "close": "17:00" }, { "day": "sunday", "closed": true } ],
    ///   "notes": "Closed on public holidays." }
    /// </code>
    ///
    /// An empty object is a business that has not given its hours, which is allowed and is told to the receptionist as plainly as the
    /// hours themselves would be: it must say it does not know rather than invent an answer. Any other shape is a failure, not skipped,
    /// for the same reason the qualifying questions are: hours that are stored and silently not read are worse than hours refused.
    /// </summary>
    internal static Result<string> BusinessHoursText(JsonElement hours)
    {
        if (hours.ValueKind == JsonValueKind.Object && !hours.EnumerateObject().Any()) return Result<string>.Success(string.Empty);
        if (hours.ValueKind == JsonValueKind.Array && hours.GetArrayLength() == 0) return Result<string>.Success(string.Empty);
        if (hours.ValueKind != JsonValueKind.Object || !hours.TryGetProperty("days", out var days) || days.ValueKind != JsonValueKind.Array)
            return Result<string>.Failure("The business hours are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);

        var text = new StringBuilder();
        if (hours.TryGetProperty("timezone", out var zone) && zone.ValueKind == JsonValueKind.String)
            text.Append("\nAll times are ").Append(zone.GetString()).Append('.');

        foreach (var day in days.EnumerateArray())
        {
            if (day.ValueKind != JsonValueKind.Object || !day.TryGetProperty("day", out var name) || name.ValueKind != JsonValueKind.String)
                return Result<string>.Failure("The business hours are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);

            if (day.TryGetProperty("closed", out var closed) && closed.ValueKind == JsonValueKind.True)
            {
                text.Append('\n').Append(name.GetString()).Append(": closed.");
                continue;
            }

            if (!day.TryGetProperty("open", out var opens) || opens.ValueKind != JsonValueKind.String
                || !day.TryGetProperty("close", out var shuts) || shuts.ValueKind != JsonValueKind.String)
                return Result<string>.Failure("The business hours are not in a form the receptionist reads.", VoiceFailureCodes.InvalidRequest);

            text.Append('\n').Append(name.GetString()).Append(": ").Append(opens.GetString()).Append(" to ").Append(shuts.GetString()).Append('.');
        }

        if (hours.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(notes.GetString()))
            text.Append('\n').Append(notes.GetString());

        return Result<string>.Success(text.ToString());
    }
}
