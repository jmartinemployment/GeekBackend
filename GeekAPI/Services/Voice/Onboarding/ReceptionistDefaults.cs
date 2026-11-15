using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// What a new tenant's receptionist starts with, until the tenant saves its own settings. The script is the one Jeff supplied, word for
/// word, except that the Role paragraph names the tenant's business, state and industry, all taken from the signup record. The
/// greeting carries the recording notice because recording_disclosure_enabled starts TRUE (Fla. Stat. 934.03, plan section 5, item 3).
/// The voice is Retell's own "retell-Lily". The settings the signup cannot supply start empty: no hours, no qualifying questions, no
/// emergency keywords, and voicemail only, because a transfer needs a number the tenant has not given.
/// </summary>
public static partial class ReceptionistDefaults
{
    public const string VoiceId = "retell-Lily";

    private const string GreetingTemplate =
        "Thank you for calling {business name}. I'm the AI receptionist, and this call may be recorded. How can I help you today?";

    private const string ScriptTemplate = """
        ## Role

        You are Lily, the virtual assistant for {business name}, a business in {state} that works in {industry}. Your job is to learn about each caller's business and needs, answer basic questions about our services, and set them up for a free consultation. Keep replies short and conversational.

        ## Call Flow Overview

        1. Greet the caller and find out what brought them in.
        2. Learn about their business and needs.
        3. Collect their contact details for a free consultation.
        4. Confirm and close.

        ## Step 1: Understand Their Needs

        Find out:
        - Their name and business name
        - What their business does
        - What they want help with, such as marketing, accounting, administration, sales, customer service calls, or general operations
        - How soon they want to get started

        Answer questions about our services using the Reference Information. If you don't know an answer, say the consultant will cover it on the consultation.

        ## Step 2: Offer a Free Consultation

        Offer a free consultation with one of our consultants. If the caller is interested, collect their best phone number, email address, and preferred days and times. Consultations are held between 9 AM and 4 PM. Read back the details to confirm them.

        Tell the caller our team will reach out to confirm the consultation time.

        ## Closing

        Once the caller has no more questions, thank them and call `end_call`. If they aren't interested, thank them for calling and call `end_call`.

        ## Reference Information

        - **Who we serve:** Small businesses in Miami-Dade, Broward, and Palm Beach.
        - **What we do:** Set up AI tools and automation that integrate with a business's existing tools. Areas include:
          - **Marketing:** AI content planning and creation, personalized email campaigns, lead enrichment and scoring, ad spend optimization.
          - **Accounting and finance:** Bookkeeping, invoice scanning, transaction categorization, accounts payable workflows, cash flow forecasting, tax compliance tools.
          - **Business operations:** AI receptionists, AI chatbots, AI workflows, AI CRM, data infrastructure.
        - **Our approach:** Define business goals, assess data quality, choose the right AI tools, and build an implementation plan. We start small, measure results, and scale.
        - **Pricing:** Depends on the project. The consultant covers it during the free consultation.

        ## Hold Handling

        If the caller asks you to wait, output exactly:

        `NO_RESPONSE_NEEDED`
        """;

    private static readonly Dictionary<string, string> StateNames = new(StringComparer.Ordinal)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "the District of Columbia", ["FL"] = "Florida",
        ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana",
        ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine",
        ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
        ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire",
        ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota",
        ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island",
        ["SC"] = "South Carolina", ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin",
        ["WY"] = "Wyoming",
    };

    /// <summary>The greeting and prompt for this tenant, or a failure when the signup record cannot fill them. Nothing is guessed.</summary>
    public static Result<RetellAgentScript> ScriptFor(VoiceTenant tenant)
    {
        var name = OneLine(tenant.BusinessName);
        var industry = OneLine(tenant.IndustryVertical);
        if (name.Length == 0 || industry.Length == 0)
            return Result<RetellAgentScript>.Failure("The business name and industry are required.", VoiceFailureCodes.InvalidRequest);
        if (!StateNames.TryGetValue(tenant.StateCode?.Trim().ToUpperInvariant() ?? "", out var state))
            return Result<RetellAgentScript>.Failure("The state code is not a US state.", VoiceFailureCodes.InvalidRequest);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["business name"] = name,
            ["state"] = state,
            ["industry"] = industry,
        };
        return Result<RetellAgentScript>.Success(new RetellAgentScript(Fill(GreetingTemplate, values), Fill(ScriptTemplate, values), VoiceId));
    }

    /// <summary>The receptionist settings a tenant starts with, holding the script the agent was created with.</summary>
    public static VoiceReceptionistConfig ConfigFor(RetellAgentScript script) => new(
        GreetingMessage: script.Greeting,
        VoiceId: script.VoiceId,
        SystemPromptTemplate: script.Prompt,
        BusinessHours: Json("{}"),
        QualifyingQuestions: Json("[]"),
        TransferMode: VoiceTransferModes.VoicemailOnly,
        TransferTargetNumber: null,
        TransferCallerIdMode: VoiceTransferCallerIdModes.OriginalCaller,
        EmergencyKeywords: [],
        RecordingDisclosureEnabled: true);

    /// <summary>The tokens a template names, so a test can prove each one is a value the signup supplies.</summary>
    internal static IReadOnlyList<string> TokensIn(string template) =>
        Token().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    internal static string GreetingText => GreetingTemplate;
    internal static string ScriptText => ScriptTemplate;

    /// <summary>One pass, so a value that itself contains a token is never filled a second time.</summary>
    private static string Fill(string template, Dictionary<string, string> values) =>
        Token().Replace(template, m => values[m.Groups[1].Value]);

    /// <summary>A name on one line with single spaces, so a signup cannot add a line to the agent's prompt.</summary>
    private static string OneLine(string? text) => Spaces().Replace(text ?? "", " ").Trim();

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [GeneratedRegex(@"\{([a-z ]+)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
