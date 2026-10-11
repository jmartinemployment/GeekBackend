using System.Text;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What the operator's research says each partner answers: the failures entered under that partner in
/// the brief, each with what the partner does about it. Told to the pages that are about every
/// partner, the pillar and the blog.
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
    /// The block naming each partner's failures and remedies, in the project's partner order, or null
    /// when the brief holds no row under any partner.
    /// </summary>
    /// <param name="briefJson">The create's brief.</param>
    /// <param name="partnerUrls">The project's declared partners.</param>
    public static string? Instruction(string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        var read = Read(briefJson, partnerUrls);
        if (read.Count == 0) return null;

        var partners = new StringBuilder();
        foreach (var (name, rows) in read)
        {
            partners.AppendLine($"{name}:");
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
        }

        return new StringBuilder()
            .AppendLine($"{Heading} -- THE OPERATOR'S OWN RESEARCH (argue from it, never cite it):")
            .AppendLine(
                "Under each partner are the failures the operator found it answers and, where the operator " +
                "noted it, what it does about each. This is the publisher's position, not retrieved " +
                "evidence: attribute none of it to a source, quote none of it, and take no figure from it.")
            .AppendLine(
                "It says which partner belongs with which failure, and what to look for in that partner's " +
                "passages in QUOTEABLE RESEARCH. Where you name a partner, name it for a failure listed " +
                "under it. Say what the partner does about that failure only as its own passages show it; " +
                "where no passage shows it, name the partner for the failure it answers and claim nothing more.")
            .AppendLine(CarryThePagesFailures(briefJson, partnerUrls)
                ? "The operator named no failure apart from these. A section told to argue where the work " +
                  "goes wrong argues it from these; everywhere else they are why a partner is named where it is."
                : "These failures are not added to the ones a section is told to cover. They are why a " +
                  "partner is named where it is.")
            .Append(partners)
            .ToString();
    }

    /// <summary>
    /// Each partner of the project that has a row of its own, as the writer is told to spell it, in
    /// the project's partner order, with those rows. A row with no failure and no remedy is not a row.
    /// </summary>
    private static IReadOnlyList<(string Name, IReadOnlyList<GccEvidenceRow> Rows)> Read(
        string? briefJson, IReadOnlyList<string>? partnerUrls)
    {
        // The same lookup the required-mentions block is built from, so a partner is spelled here as
        // the writer is told to spell it there.
        var names = GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls);
        if (names.Count == 0) return [];

        var partners = new List<(string Name, IReadOnlyList<GccEvidenceRow> Rows)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in partnerUrls ?? [])
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length == 0 || !seen.Add(host)) continue;
            if (!names.TryGetValue(host, out var name)) continue;

            var rows = GccNicheFramingReader.OwnRowsForHost(briefJson, host)
                .Where(row => row.Problem.Length > 0 || row.Solution.Length > 0)
                .ToList();
            if (rows.Count > 0) partners.Add((name, rows));
        }

        return partners;
    }
}
