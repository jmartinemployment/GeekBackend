using System.Text;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What the operator's research says each partner answers: the problem it is for, the failures entered
/// under that partner in the brief, each with what the partner does about it, and what the operator
/// would pitch it on. Told to the pages that are about every partner, the pillar and the blog
/// (<see cref="Instruction"/>), and to each partner's own page (<see cref="ForToolPage"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The operator researches one core problem for every partner and then, per partner, the
/// failures it answers and its remedy for each. Those rows were read for that partner's search and
/// for that partner's tool page and for nothing else: the pillar and the blog were told to name every
/// partner (<see cref="GccRequiredToolMentions.Instruction"/>) and given no word of which partner
/// answers which failure, so the writer matched them from several hundred passages on its own. Jeff,
/// 2026-10-11, asked whether the pillar and the blog should be given each partner's failures and
/// remedies: yes.
/// </para>
/// <para>
/// <b>The operator's note, not evidence.</b> A remedy is the operator's description of what a vendor
/// does, entered so the vendor's crawl could be searched for it. What that search returned is the
/// evidence, and it is already in the page's research. The note says which partner belongs with which
/// failure and what to look for in that partner's passages; the writer is told to say what a partner
/// does only as those passages show it. No code checks a sentence about a partner against a passage,
/// here or anywhere else.
/// </para>
/// <para>
/// <b>A partner's own problem and its pitch, since 2026-10-12.</b> The two boxes a partner has
/// besides its rows ("Core pain point" and "The automation to pitch — the marketing/sales hook" in
/// that tool's section of the brief) reached that partner's tool page and no other page. Jeff,
/// 2026-10-12: a pillar or a blog discussing a tool should be able to use them ("pillars &amp; blog
/// should have the research data"). Each is printed under the partner when the operator entered one
/// for it; the category's is not printed there in its place, because the page already carries the
/// category's as its own framing. A pitch is a selling line and may name a capability or a figure:
/// like a remedy it is the operator's claim, and the writer is told to state of a partner only what
/// its passages show.
/// </para>
/// <para>
/// <b>Nothing entered is left out, since 2026-10-12</b> (Jeff: "WHY am I spending so much time
/// entering and you not using"). Three things were entered and shown to no writer, or to fewer than
/// they concern:
/// <list type="bullet">
/// <item>A row saved for all partners: its failure reached the pages as one their sections cover
/// (<see cref="GccNicheFraming.FailuresGuidance"/>) and its remedy reached none. It is listed here
/// with its remedy, under no partner's name, on every page.</item>
/// <item>A partner's failures saved without a row, as a brief saved before 2026-10-08 holds them:
/// they reached that partner's own page alone. They are listed under the partner with its rows
/// (<see cref="GccNicheFraming.Failures"/>).</item>
/// <item>A partner's own page was told its failures and never the remedy noted for each, and was
/// told them in the first of its ten sections alone. It is given a block of its own, in every body
/// request (<see cref="ForToolPage"/>).</item>
/// </list>
/// </para>
/// <para>
/// <b>A brief with no row for all partners.</b> The operator's research is per partner, and a brief
/// entered that way has every failure under a partner and none for the category. The sections of
/// the pillar and the blog that argue where the work goes wrong were then told no failure at all:
/// on the 13:14 UTC run of 2026-10-11, the first brief entered that way, 25 failures were in the
/// brief and neither page was told one. Since then those sections argue from the partners' failures
/// (<see cref="CarryThePagesFailures"/>, Jeff, 2026-10-11), and this block says so.
/// </para>
/// </remarks>
public static class GccPartnerAnswers
{
    /// <summary>
    /// How the block is headed. A section told to argue from the partners' failures is sent here by
    /// this name (<see cref="GccNicheFraming.FailuresGuidance"/>), so the two are one string.
    /// </summary>
    public const string Heading = "WHAT EACH PARTNER TOOL ANSWERS";

    /// <summary>
    /// How a tool page's own block is headed. Its opening section is sent here by this name
    /// (<see cref="ToolOpeningGuidance"/>), so the two are one string.
    /// </summary>
    public const string ToolPageHeading = "THE OPERATOR'S RESEARCH FOR THIS PAGE";

    /// <summary>What a failure's line is labelled, for the same reason.</summary>
    public const string FailureLabel = "Where it goes wrong";

    /// <summary>How the line that carries a partner's own problem begins, before the partner's name.</summary>
    public const string ProblemLabel = "The problem";

    /// <summary>How the line that carries what a partner is pitched on begins, before the partner's name.</summary>
    public const string PitchLabel = "What the operator would pitch";

    /// <summary>What the failures entered for all partners are listed under, where a partner's name would be.</summary>
    public const string EveryPartnerLabel = "Every partner";

    /// <summary>How the line that carries a remedy entered for all partners begins.</summary>
    public const string EveryPartnerRemedyLabel = "The remedy noted for every partner";

    /// <summary>
    /// Whether the failures a page about every partner argues are the ones under the partners: the
    /// operator entered none for the category, and at least one under a partner of this project.
    /// </summary>
    /// <remarks>
    /// The one answer both readers take. The block words its last sentence by it, and the framing the
    /// pillar and the blog are handed sends their failure sections to the block by it
    /// (<see cref="GccNicheFramingReader.ForEveryPartner"/>), so a section is never sent to a block
    /// that lists no failure and the block never disowns the failures a section was sent for.
    /// </remarks>
    public static bool CarryThePagesFailures(string? briefJson, IReadOnlyList<string>? partnerUrls) =>
        GccNicheFramingReader.ForCategory(briefJson) is not { PainPoints.Count: > 0 }
        && Read(briefJson, partnerUrls).Any(partner => partner.Failures.Any(failure => failure.Problem.Length > 0));

    /// <summary>
    /// The block naming each partner's problem, failures, remedies and pitch, in the project's partner
    /// order, then the failures entered for all partners that carry a remedy. Null when the brief
    /// holds none of them, or the project has no partner.
    /// </summary>
    /// <param name="briefJson">The create's brief.</param>
    /// <param name="partnerUrls">The project's declared partners.</param>
    public static string? Instruction(string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        if (GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls).Count == 0) return null;

        var read = Read(briefJson, partnerUrls);
        // A failure for all partners with no remedy noted is already the pages': their sections are
        // told to cover it. What is listed here is what they were never told, the remedy, with the
        // failure it is for beside it.
        var forAll = (GccNicheFramingReader.ForCategory(briefJson)?.Failures() ?? [])
            .Where(failure => failure.Remedy.Length > 0)
            .ToList();
        if (read.Count == 0 && forAll.Count == 0) return null;

        var listed = new StringBuilder();
        foreach (var (name, problem, failures, pitch) in read)
        {
            listed.AppendLine($"{name}:");
            if (problem.Length > 0) listed.AppendLine($"  {ProblemLabel} {name} is for: {problem}");
            for (var i = 0; i < failures.Count; i++)
            {
                var failure = failures[i];
                if (failure.Problem.Length > 0)
                {
                    listed.AppendLine($"  {i + 1}. {FailureLabel}: {failure.Problem}");
                    if (failure.Remedy.Length > 0)
                    {
                        listed.AppendLine($"     What {name} does about it: {failure.Remedy}");
                    }
                }
                else
                {
                    listed.AppendLine($"  {i + 1}. What {name} does: {failure.Remedy}");
                }
            }

            if (pitch.Length > 0) listed.AppendLine($"  {PitchLabel} {name} on: {pitch}");
        }

        if (forAll.Count > 0)
        {
            listed.AppendLine($"{EveryPartnerLabel}:");
            for (var i = 0; i < forAll.Count; i++)
            {
                var failure = forAll[i];
                if (failure.Problem.Length > 0)
                {
                    listed.AppendLine($"  {i + 1}. {FailureLabel}: {failure.Problem}");
                    listed.AppendLine($"     {EveryPartnerRemedyLabel}: {failure.Remedy}");
                }
                else
                {
                    listed.AppendLine($"  {i + 1}. {EveryPartnerRemedyLabel}: {failure.Remedy}");
                }
            }
        }

        const string notEvidence =
            "This is the publisher's position, not retrieved evidence: attribute none of it to a source, " +
            "quote none of it, and take no figure from it.";
        var block = new StringBuilder()
            .AppendLine($"{Heading} -- THE OPERATOR'S OWN RESEARCH (argue from it, never cite it):");

        if (read.Count > 0)
        {
            block
                .AppendLine(
                    "Under each partner are the failures the operator found it answers and, where the operator " +
                    "noted it, what it does about each; and, where the operator entered them, the problem the " +
                    "partner is for and what the operator would pitch it on. " + notEvidence)
                .AppendLine(
                    "It says which partner belongs with which failure, and what to look for in that partner's " +
                    "passages in QUOTEABLE RESEARCH. Where you name a partner, name it for a failure listed " +
                    "under it, or for its problem where no failure is. Say what the partner does about that " +
                    "failure only as its own passages show it; " +
                    "where no passage shows it, name the partner for the failure it answers and claim nothing more.")
                .AppendLine(
                    "What a partner is pitched on is the case to make for it where you discuss that partner, in " +
                    "your own words and never verbatim. A capability it names is the operator's claim like any " +
                    "other line here: state it only as that partner's passages show it.")
                .AppendLine(CarryThePagesFailures(briefJson, partnerUrls)
                    ? "The operator named no failure apart from these. A section told to argue where the work " +
                      "goes wrong argues it from these; everywhere else they are why a partner is named where it is."
                    : "These failures are not added to the ones a section is told to cover. They are why a " +
                      "partner is named where it is.");
        }

        if (forAll.Count > 0)
        {
            block.AppendLine(
                $"Under \"{EveryPartnerLabel}\" are failures the operator entered for all of them, each with the " +
                "remedy noted for it. " + (read.Count > 0 ? string.Empty : notEvidence + " ") +
                "A failure there is one a section is already told to cover, repeated here for its remedy. A " +
                "remedy there names no partner: it is what to look for in each partner's passages in QUOTEABLE " +
                "RESEARCH, and a partner is said to do it only where that partner's own passages show it.");
        }

        return block.Append(listed).ToString();
    }

    /// <summary>
    /// What a tool page's opening section is told when the page carries <see cref="ForToolPage"/>:
    /// where the operator's framing is, and that the section covers the problem and the failures.
    /// </summary>
    /// <remarks>
    /// The opening section carried the framing itself until 2026-10-12, in its own guidance, which the
    /// first of the page's five body requests alone prints. The framing is now in a block every body
    /// request carries, and the section is sent to it, as the pillar's failure sections are sent to
    /// <see cref="Heading"/>. One copy in a request, not two.
    /// </remarks>
    public const string ToolOpeningGuidance =
        "THE OPERATOR'S OWN FRAMING OF THIS PROBLEM is in " + ToolPageHeading + " -- argue from it, never " +
        "cite it. This section covers what is stated there of the problem and of where it goes wrong, every " +
        "failure listed, in your own prose, never as a list, and never verbatim.";

    /// <summary>
    /// The block one tool's own page is told: the problem as entered for every partner and as entered
    /// for this tool, every failure with the remedy noted for it, and what the operator would pitch,
    /// for every partner and for this tool. Null when the brief holds none of them.
    /// </summary>
    /// <param name="framing">The category's framing and the tool's own section
    /// (<see cref="GccNicheFramingReader.ForProduct"/>).</param>
    /// <param name="product">The tool, as its page names it.</param>
    /// <remarks>
    /// <para>
    /// <b>Both statements, never one for the other.</b> "The core problem" and this tool's "Core pain
    /// point" are both printed, and so are "The automation to pitch" and this tool's own pitch. Until
    /// 2026-10-12 the tool's own was printed in the category's place (Jeff: "it is not an OR it is
    /// both PAIN POINTS").
    /// </para>
    /// <para>
    /// <b>Every failure once, the category's first.</b> A failure the operator entered for every
    /// partner and again under this tool is one failure, with the tool's own remedy and the one noted
    /// for every partner both under it.
    /// </para>
    /// <para>
    /// <b>In every body request.</b> <c>ToolPrompts.Body</c> adds it to each request's evidence, so the
    /// ten sections are all written within it, where the first alone was. The operator's note, never
    /// evidence: it is not among what the page's quotation and link checks read.
    /// </para>
    /// </remarks>
    public static string? ForToolPage(GccToolFraming? framing, string? product)
    {
        if (framing is null) return null;

        var name = string.IsNullOrWhiteSpace(product) ? "this product" : product.Trim();
        var problem = (framing.Category?.CoreProblem ?? string.Empty).Trim();
        var ownProblem = Besides(problem, framing.Own?.CoreProblem);
        var pitch = (framing.Category?.AutomationToPitch ?? string.Empty).Trim();
        var ownPitch = Besides(pitch, framing.Own?.AutomationToPitch);
        var failures = ToolFailures(framing);
        if (problem.Length == 0 && ownProblem.Length == 0 && pitch.Length == 0 && ownPitch.Length == 0 && failures.Count == 0)
        {
            return null;
        }

        var block = new StringBuilder()
            .AppendLine($"{ToolPageHeading} -- THE OPERATOR'S OWN RESEARCH (argue from it, never cite it):")
            .AppendLine(
                $"What the operator entered on the brief for every partner and for {name} itself: the problem, " +
                "where it goes wrong with the remedy noted for each failure, and what the operator would pitch. " +
                "This is the publisher's position, not retrieved evidence: attribute none of it to a source, " +
                "quote none of it, and take no figure from it.")
            .AppendLine(
                "Every section of this page is written within it. The opening section covers the problem and " +
                "where it goes wrong; a later section does not restate them, and where it touches one of these " +
                $"failures it says what {name} does about that failure.");
        if (failures.Any(failure => failure.OwnRemedy.Length > 0 || failure.EveryPartnerRemedy.Length > 0))
        {
            block.AppendLine(
                $"A remedy is what to look for in {name}'s own pages in the evidence. Say what {name} does about " +
                $"a failure only as its own pages show it; where none shows it, claim nothing about what {name} " +
                "does for that failure.");
        }

        if (pitch.Length > 0 || ownPitch.Length > 0)
        {
            block.AppendLine(
                $"What the operator would pitch is the case this page makes for {name}, in your own words and " +
                "never verbatim. A capability it names is the operator's claim like any other line here: state " +
                $"it only as {name}'s own pages show it.");
        }

        if (problem.Length > 0) block.AppendLine($"{ProblemLabel}: {problem}");
        if (ownProblem.Length > 0) block.AppendLine($"{name}'s own pain point: {ownProblem}");
        for (var i = 0; i < failures.Count; i++)
        {
            var (failure, ownRemedy, everyPartnerRemedy) = failures[i];
            if (failure.Length > 0)
            {
                block.AppendLine($"  {i + 1}. {FailureLabel}: {failure}");
                if (ownRemedy.Length > 0) block.AppendLine($"     What {name} does about it: {ownRemedy}");
                if (everyPartnerRemedy.Length > 0) block.AppendLine($"     {EveryPartnerRemedyLabel}: {everyPartnerRemedy}");
            }
            else if (ownRemedy.Length > 0)
            {
                block.AppendLine($"  {i + 1}. What {name} does: {ownRemedy}");
            }
            else
            {
                block.AppendLine($"  {i + 1}. {EveryPartnerRemedyLabel}: {everyPartnerRemedy}");
            }
        }

        if (pitch.Length > 0) block.AppendLine($"What answers it: {pitch}");
        if (ownPitch.Length > 0) block.AppendLine($"{PitchLabel} {name} on: {ownPitch}");
        return block.ToString().TrimEnd();
    }

    /// <summary>
    /// A tool's own statement, or empty when it is the category's word for word: one statement entered
    /// in both boxes is one statement.
    /// </summary>
    private static string Besides(string categorys, string? own)
    {
        var stated = (own ?? string.Empty).Trim();
        return string.Equals(stated, categorys, StringComparison.OrdinalIgnoreCase) ? string.Empty : stated;
    }

    /// <summary>
    /// The failures a tool's page is told, each once: the category's in the operator's order, then the
    /// tool's own that the category does not name. A failure named by both carries both remedies.
    /// </summary>
    private static IReadOnlyList<(string Failure, string OwnRemedy, string EveryPartnerRemedy)> ToolFailures(GccToolFraming framing)
    {
        var category = framing.Category?.Failures() ?? [];
        var own = framing.Own?.Failures() ?? [];
        var placed = new bool[own.Count];
        var failures = new List<(string Failure, string OwnRemedy, string EveryPartnerRemedy)>(category.Count + own.Count);
        foreach (var failure in category)
        {
            // The tool's own entry for this failure, when it has one. A second row of the tool's for the
            // same failure is not folded in here: it is listed after, with its own remedy.
            var at = -1;
            for (var i = 0; i < own.Count && at < 0 && failure.Problem.Length > 0; i++)
            {
                if (!placed[i] && string.Equals(own[i].Problem, failure.Problem, StringComparison.OrdinalIgnoreCase)) at = i;
            }

            if (at >= 0) placed[at] = true;
            failures.Add((failure.Problem, at >= 0 ? own[at].Remedy : string.Empty, failure.Remedy));
        }

        for (var i = 0; i < own.Count; i++)
        {
            if (!placed[i]) failures.Add((own[i].Problem, own[i].Remedy, string.Empty));
        }

        return failures;
    }

    /// <summary>
    /// Each partner of the project the operator entered something of its own for, as the writer is
    /// told to spell it, in the project's partner order: its own problem, its failures with the remedy
    /// noted for each, and what to pitch it on. A partner with none of the three is not listed.
    /// </summary>
    private static IReadOnlyList<(string Name, string Problem, IReadOnlyList<GccFailure> Failures, string Pitch)> Read(
        string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        // The same lookup the required-mentions block is built from, so a partner is spelled here as
        // the writer is told to spell it there.
        var names = GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls);
        if (names.Count == 0) return [];

        var partners = new List<(string Name, string Problem, IReadOnlyList<GccFailure> Failures, string Pitch)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in partnerUrls ?? [])
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length == 0 || !seen.Add(host)) continue;
            if (!names.TryGetValue(host, out var name)) continue;

            var own = GccNicheFramingReader.OwnForHost(briefJson, host);
            if (own is null) continue;

            var problem = own.CoreProblem.Trim();
            var pitch = own.AutomationToPitch.Trim();
            var failures = own.Failures();
            if (failures.Count > 0 || problem.Length > 0 || pitch.Length > 0) partners.Add((name, problem, failures, pitch));
        }

        return partners;
    }
}
