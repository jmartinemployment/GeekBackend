namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The part of a pillar or a blog a passage of research is for
/// (plans/research-classification.md, "The labels").
/// </summary>
/// <remarks>
/// <para>
/// Seven, matched to what the two pages' sections are about. A section names the one it is written
/// from (<c>SectionSlot.ResearchLabel</c>, set in <c>PillarPrompts</c> and <c>BlogPrompts</c>), and a
/// passage is given the ones its page, its searches and its words point at
/// (<see cref="GccPassageClassifier"/>). A passage may carry several.
/// </para>
/// <para>
/// The names are the run log's, not a writer's: no prompt prints one.
/// </para>
/// </remarks>
public static class GccResearchLabel
{
    /// <summary>The problem and what it costs.</summary>
    public const string Problem = "problem";

    /// <summary>How it works.</summary>
    public const string HowItWorks = "how-it-works";

    /// <summary>Getting it running: what makes an implementation hold up, the early decisions, the rollout.</summary>
    public const string GettingItRunning = "getting-it-running";

    /// <summary>Data and integrations.</summary>
    public const string DataAndIntegrations = "data-and-integrations";

    /// <summary>People and roles.</summary>
    public const string PeopleAndRoles = "people-and-roles";

    /// <summary>Results and proof.</summary>
    public const string ResultsAndProof = "results-and-proof";

    /// <summary>Fit, price and limits.</summary>
    public const string FitPriceAndLimits = "fit-price-and-limits";

    /// <summary>
    /// What a passage with no clear label is recorded as. Not one of the seven and never a section's:
    /// a general passage is shared among every request.
    /// </summary>
    public const string General = "general";

    /// <summary>The seven, in the order the plan lists them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Problem, HowItWorks, GettingItRunning, DataAndIntegrations, PeopleAndRoles, ResultsAndProof, FitPriceAndLimits,
    ];
}
