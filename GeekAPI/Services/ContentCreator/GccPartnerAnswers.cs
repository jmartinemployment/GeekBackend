using System.Text;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What the operator's research says each partner answers: the problem it is for, the failures entered
/// under that partner in the brief, each with what the partner does about it, and what the operator
/// would pitch it on. Told to the pages that are about every partner, the pillar and the blog.
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
/// besides its rows ("The core problem" and "The automation to pitch" in that tool's section of the
/// brief) reached that partner's tool page and no other page. Jeff, 2026-10-12: a pillar or a blog
/// discussing a tool should be able to use them ("pillars &amp; blog should have the research data").
/// Each is printed under the partner when the operator entered one for it; the category's is not
/// printed here in its place, because the page already carries the category's as its own framing. A
/// pitch is a selling line and may name a capability or a figure: like a remedy it is the operator's
/// claim, and the writer is told to state of a partner only what its passages show.
/// </para>
/// <para>
/// <b>Only a partner's own rows.</b> A row entered for all partners is the category's, and reaches
/// these pages as the failures their sections cover (<see cref="GccNicheFraming.FailuresGuidance"/>).
/// It names no partner, so it has no place here.
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

    /// <summary>What a failure's line is labelled, for the same reason.</summary>
    public const string FailureLabel = "Where it goes wrong";

    /// <summary>How the line that carries a partner's own problem begins, before the partner's name.</summary>
    public const string ProblemLabel = "The problem";

    /// <summary>How the line that carries what a partner is pitched on begins, before the partner's name.</summary>
    public const string PitchLabel = "What the operator would pitch";

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
        && Read(briefJson, partnerUrls).Any(partner => partner.Rows.Any(row => row.Problem.Length > 0));

    /// <summary>
    /// The block naming each partner's problem, failures, remedies and pitch, in the project's partner
    /// order, or null when the brief holds none of them under any partner.
    /// </summary>
    /// <param name="briefJson">The create's brief.</param>
    /// <param name="partnerUrls">The project's declared partners.</param>
    public static string? Instruction(string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        var read = Read(briefJson, partnerUrls);
        if (read.Count == 0) return null;

        var partners = new StringBuilder();
        foreach (var (name, rows, problem, pitch) in read)
        {
            partners.AppendLine($"{name}:");
            if (problem.Length > 0) partners.AppendLine($"  {ProblemLabel} {name} is for: {problem}");
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Problem.Length > 0)
                {
                    partners.AppendLine($"  {i + 1}. {FailureLabel}: {row.Problem}");
                    if (row.Solution.Length > 0)
                    {
                        partners.AppendLine($"     What {name} does about it: {row.Solution}");
                    }
                }
                else
                {
                    partners.AppendLine($"  {i + 1}. What {name} does: {row.Solution}");
                }
            }

            if (pitch.Length > 0) partners.AppendLine($"  {PitchLabel} {name} on: {pitch}");
        }

        return new StringBuilder()
            .AppendLine($"{Heading} -- THE OPERATOR'S OWN RESEARCH (argue from it, never cite it):")
            .AppendLine(
                "Under each partner are the failures the operator found it answers and, where the operator " +
                "noted it, what it does about each; and, where the operator entered them, the problem the " +
                "partner is for and what the operator would pitch it on. This is the publisher's position, " +
                "not retrieved evidence: attribute none of it to a source, quote none of it, and take no " +
                "figure from it.")
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
                  "partner is named where it is.")
            .Append(partners)
            .ToString();
    }

    /// <summary>
    /// Each partner of the project the operator entered something of its own for, as the writer is
    /// told to spell it, in the project's partner order: its rows, its own problem and what to pitch
    /// it on. A row with no failure and no remedy is not a row, and a partner with none of the three
    /// is not listed.
    /// </summary>
    private static IReadOnlyList<(string Name, IReadOnlyList<GccEvidenceRow> Rows, string Problem, string Pitch)> Read(
        string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        // The same lookup the required-mentions block is built from, so a partner is spelled here as
        // the writer is told to spell it there.
        var names = GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls);
        if (names.Count == 0) return [];

        var partners = new List<(string Name, IReadOnlyList<GccEvidenceRow> Rows, string Problem, string Pitch)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in partnerUrls ?? [])
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length == 0 || !seen.Add(host)) continue;
            if (!names.TryGetValue(host, out var name)) continue;

            var rows = GccNicheFramingReader.OwnRowsForHost(briefJson, host)
                .Where(row => row.Problem.Length > 0 || row.Solution.Length > 0)
                .ToList();
            var (problem, pitch) = GccNicheFramingReader.OwnStatementsForHost(briefJson, host) ?? (string.Empty, string.Empty);
            if (rows.Count > 0 || problem.Length > 0 || pitch.Length > 0) partners.Add((name, rows, problem, pitch));
        }

        return partners;
    }
}
