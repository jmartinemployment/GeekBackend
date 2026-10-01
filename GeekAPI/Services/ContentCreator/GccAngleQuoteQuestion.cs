namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a tool page's block quotation has to <i>answer</i>, derived from the brief's Angle for SEO.
/// </summary>
/// <remarks>
/// <para>
/// A block quotation is not "any verbatim partner sentence". Jeff, 2026-10-01: <i>"The Brief
/// contains the Angle for SEO, which is used to obtain the type of question that the blockquote
/// answers … You are seeking a blockquote with a cite to provide an answer the problem with Manual
/// Data Entry &amp; Processing that a Partner fixes."</i> So for <c>problem_solution</c> on
/// "Accounts Payable: Automated Data Entry &amp; Processing", the quote has to evidence the pain of
/// <i>manual</i> data entry that a partner resolves — not merely be something the partner said.
/// </para>
/// <para>
/// The vocabulary is closed and already normalised upstream: <c>GccGenerateService.LegacyAngleMap</c>
/// folds every legacy spelling onto these four. Anything else, including a null angle, has no
/// question — and that is reported rather than guessed, because inventing a default angle would
/// quietly validate a partner against a question the brief never asked.
/// </para>
/// <para>
/// Two strings per angle, deliberately. <see cref="Need"/> is a retrieval query: it is matched
/// against chunk text by embedding similarity, so it reads as the subject matter being looked for.
/// <see cref="Rule"/> is an instruction to the selector about which span qualifies once candidates
/// are back. Retrieval that is too prescriptive returns nothing; selection that is too loose returns
/// anything.
/// </para>
/// </remarks>
public static class GccAngleQuoteQuestion
{
    public const string ProblemSolution = "problem_solution";
    public const string Comparative = "comparative";
    public const string CaseStudyData = "case_study_data";
    public const string UltimateGuide = "ultimate_guide";

    /// <summary>The question for one angle, or null when the angle is absent or unrecognised.</summary>
    public static GccAngleQuoteSpec? For(string? angle, string? topic)
    {
        var subject = (topic ?? string.Empty).Trim();
        if (subject.Length == 0) return null;

        return (angle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ProblemSolution => new GccAngleQuoteSpec(
                ProblemSolution,
                $"The cost, delay, error rate and frustration of doing {subject} manually, and how "
                    + "it is fixed",
                $"a major pain point of the manual or status-quo way of handling {subject}, and how "
                    + "this partner's product resolves it"),

            Comparative => new GccAngleQuoteSpec(
                Comparative,
                $"What makes this product different from other ways of doing {subject}",
                $"a differentiator, capability or advantage of this partner for {subject}, set "
                    + "against competitors or against traditional methods"),

            CaseStudyData => new GccAngleQuoteSpec(
                CaseStudyData,
                $"Measured results a named customer achieved with {subject} — time saved, cost "
                    + "reduced, volume handled",
                $"tangible proof about {subject} — data, a metric, or a named customer's success "
                    + "story or testimonial"),

            UltimateGuide => new GccAngleQuoteSpec(
                UltimateGuide,
                $"What {subject} is, what it requires, and how it is done properly",
                $"an authoritative or definition-style statement explaining a core concept or best "
                    + $"practice of {subject}"),

            _ => null,
        };
    }
}

/// <summary>
/// One angle's question: what to retrieve, and which span qualifies once it is retrieved.
/// </summary>
/// <param name="Angle">The normalised angle this came from, for the operator-facing message.</param>
/// <param name="Need">
/// The retrieval query. Free text matched against chunk text — see
/// <c>GccGroundingResolver</c>, which already passes a need of this shape to
/// <c>IGeekCrawlerRagClient.QueryAsync</c>.
/// </param>
/// <param name="Rule">What the selector must look for among the retrieved candidates.</param>
public sealed record GccAngleQuoteSpec(string Angle, string Need, string Rule);
