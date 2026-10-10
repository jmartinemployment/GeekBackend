namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

/// <summary>
/// A choice from the brief, in words a writer can act on.
/// </summary>
/// <remarks>
/// <para>
/// The form stores each choice as a code (<c>in_market</c>, <c>commercial_investigation</c>,
/// <c>consultant_professional</c>), and until 2026-10-10 the code is what every prompt printed: "WHO
/// THIS IS FOR: your_data". The audience codes are Google Ads audience segments, which say nothing to
/// a writer, and two of the lines carried a legend of every option beside the one chosen, which is
/// the shape that once had a model return a brief value as its answer. Only the angle was put into
/// words. Jeff, 2026-10-10: "Incorporate and use everything from Brief."
/// </para>
/// <para>
/// One place, read by every prompt that prints the brief, so a choice cannot read one way in the
/// opening and another in the body. A value that is not one of the form's codes is the operator's own
/// text (an older brief, or the Workflow product) and is printed as typed.
/// </para>
/// </remarks>
public static class BriefChoiceWords
{
    /// <summary>
    /// The angle, as a line of its own: what it is and what it asks of the piece. It was the one choice
    /// already put into words, by the opening calls only; the other blocks printed its code.
    /// </summary>
    public static string Angle(string? value) => Key(value) switch
        {
            "problem_solution" =>
                "Angle -- Problem-Solution: open on the reader's problem and what it is costing them, "
                + "then show how this resolves it. The problem is the hook, not a preamble; earn the "
                + "solution by making the cost concrete first.",
            "comparative" =>
                "Angle -- Comparative (\"versus\"): frame against the alternatives this reader is "
                + "actually weighing. The value is in the contrast and the trade-offs, never a feature "
                + "list that ignores what else they could do.",
            "case_study_data" =>
                "Angle -- Case Study / Data-Driven: lead with evidence -- a number, an outcome, a "
                + "documented result -- and let the argument follow from it. Never invent a figure to "
                + "carry this angle; if the evidence is not in what you were given, argue from what is.",
            "ultimate_guide" =>
                "Angle -- Comprehensive \"Ultimate Guide\": the promise is completeness. Breadth and "
                + "structure carry it: cover the whole territory in an order a reader can follow.",
            _ => $"Angle: {Typed(value)}",
        };

    /// <summary>Who the page is for. The codes are Google Ads audience segments.</summary>
    public static string Audience(string? value) => Key(value) switch
    {
        "affinity" => "affinity (a lasting interest in the subject, not shopping for it yet)",
        "in_market" => "in-market (actively researching and comparing options to buy now)",
        "life_events" => "life events (at a milestone that creates the need: a new business, a move, a change of role)",
        "detailed_demographics" => "detailed demographics (defined by who they are: role, company size, industry)",
        "your_data" => "your data (already know this publisher: past visitors and existing customers)",
        "custom" => "custom (defined by the notes)",
        _ => Typed(value),
    };

    /// <summary>The search intent the page answers.</summary>
    public static string Intent(string? value) => Key(value) switch
    {
        "informational" => "informational (the reader wants to understand the subject)",
        "navigational" => "navigational (the reader is looking for a specific product, page or brand)",
        "commercial_investigation" => "commercial investigation (the reader is weighing options before buying)",
        "transactional" => "transactional (the reader is ready to act)",
        _ => Typed(value),
    };

    /// <summary>The second intent, where the operator chose one.</summary>
    public static string SecondaryIntent(string? value) => Key(value) switch
    {
        "local" => "local (they want it near them)",
        "freebies" => "freebies (they are looking for something free to start with)",
        "comparison" => "comparison (they are comparing named alternatives)",
        _ => Typed(value),
    };

    /// <summary>Where the reader is in the decision, and what that asks of the page.</summary>
    public static string Stage(string? value) => Key(value) switch
    {
        "awareness" => "awareness (top of funnel: the reader is still naming the problem, so educate)",
        "consideration" => "consideration (middle of funnel: the reader is weighing ways to solve it, so compare)",
        "action" => "action (bottom of funnel: the reader is choosing who to act with, so make the next step plain)",
        _ => Typed(value),
    };

    /// <summary>The voice the page is written in.</summary>
    public static string Tone(string? value) => Key(value) switch
    {
        "consultant_professional" => "consultant, professional (objective authority)",
        "informational_instructional" => "informational, instructional (clear and stepwise)",
        "commercial_balanced" => "commercial, balanced (benefits weighed against their trade-offs)",
        _ => Typed(value),
    };

    /// <summary>One E-E-A-T signal.</summary>
    public static string Eeat(string? value) => Key(value) switch
    {
        "first_hand_experience" => "first-hand experience",
        "expertise" => "expertise",
        "authoritativeness" => "authoritativeness",
        "trustworthiness" => "trustworthiness",
        _ => Typed(value),
    };

    /// <summary>The E-E-A-T signals, in the order chosen.</summary>
    public static string Eeat(IEnumerable<string> values) => string.Join(", ", values.Select(Eeat));

    private static string Key(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string Typed(string? value) => (value ?? string.Empty).Trim();
}
