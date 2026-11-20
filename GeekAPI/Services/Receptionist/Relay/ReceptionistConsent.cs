using System.Text;
using System.Text.Json;
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
    /// The plan's passive disclosure, word for word, with the business name. It ends by asking what the caller wants, so it is both the
    /// disclosure and the greeting: ConversationRelay speaks it and the AI takes the call from there without greeting twice.
    /// </summary>
    public static string DisclosureText(string business) =>
        $"Hi, you've reached {business}'s AI assistant. Calls are processed in real time and not recorded. How can I help you today?";

    /// <summary>Jeff's approved wording, word for word, with the business name.</summary>
    public static string ConsentText(string business) =>
        $"Thank you for calling {business}. You will be interacting with our AI receptionist. Your audio will be temporarily recorded and processed to provide this service, but we do not retain call recordings or transcripts. To consent and continue, press 1. To decline, press 2.";

    /// <summary>
    /// Asks for consent and posts the key to <paramref name="action"/>. With no key, Twilio does not post and goes on to the next verb:
    /// the closing message and a hang-up.
    /// </summary>
    public static VoiceResponse Ask(string business, Uri action)
    {
        var response = new VoiceResponse();
        var gather = new Gather(
            input: [Gather.InputEnum.Dtmf],
            numDigits: 1,
            action: action,
            method: HttpMethod.Post);
        gather.Say(ConsentText(business));
        response.Append(gather);
        response.Say(ReceptionistEndpoints.UnansweredText(business));
        response.Hangup();
        return response;
    }

    /// <summary>True only for a 1. A 2, nothing, or any other key declines.</summary>
    public static bool Consented(string? digits) => string.Equals(digits, ConsentDigit, StringComparison.Ordinal);

    /// <summary>
    /// Hands the call to ConversationRelay at <paramref name="relayUrl"/>, which speaks <paramref name="greeting"/> first. Only the
    /// documented url and welcomeGreeting are set: no intelligenceService, the one attribute Twilio documents for persisting transcripts.
    /// </summary>
    public static VoiceResponse Connect(string relayUrl, string greeting)
    {
        var response = new VoiceResponse();
        var connect = new Connect();
        connect.Append(new ConversationRelay(url: relayUrl, welcomeGreeting: greeting));
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

        var questions = config.QualifyingQuestions;
        if (questions.ValueKind == JsonValueKind.Array && questions.GetArrayLength() == 0)
            return Result<string>.Success(prompt.ToString());

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

    /// <summary>
    /// The company line's receptionist settings, built in code as <see cref="BillingExemption"/> builds the company tenant's: the
    /// starting script with <see cref="CompanyCallerQuestions"/>. 865-6990 has no tenant id to read stored settings by.
    /// </summary>
    public static Result<VoiceReceptionistConfig> CompanyConfig(string companyName)
    {
        var script = ReceptionistDefaults.ScriptFor(companyName, CompanyStateCode, CompanyIndustry);
        if (!script.IsSuccess)
            return Result<VoiceReceptionistConfig>.Failure(script.Error ?? "The company script could not be filled.", script.ErrorCode ?? VoiceFailureCodes.InvalidRequest);
        return Result<VoiceReceptionistConfig>.Success(
            ReceptionistDefaults.ConfigFor(script.Value!) with { QualifyingQuestions = CompanyCallerQuestions.ToJson() });
    }

    /// <summary>The script's own Reference Information serves Miami-Dade, Broward and Palm Beach.</summary>
    internal const string CompanyStateCode = "FL";

    /// <summary>The script's own "What we do" line, in a few words.</summary>
    internal const string CompanyIndustry = "AI tools and automation for small businesses";
}
