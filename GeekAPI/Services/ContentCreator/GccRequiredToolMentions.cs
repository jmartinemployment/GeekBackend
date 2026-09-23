using System.Text;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The partner tools a long-form page must name, and the check that it did.
///
/// <para>
/// Naming partners is the commercial reason this product exists -- the pieces are how a paying
/// partner gets discussed -- so a page that quietly covers two of five has not half-succeeded, it
/// has failed a contractual obligation while looking finished. Jeff, 2026-09-23: "two out of five
/// Partner/Tool mentions is less than 50%", then "I want all 5 tools listed", then "Which is the
/// same requirement Pillar will have."
/// </para>
///
/// <para>
/// Asked and answered in one place. The prompt states the list; this verifies the output against
/// the same list, so the instruction and the check can never describe different sets. Nothing
/// else on the Create path told a pillar or a blog which partners existed at all -- the names it
/// happened to use came from whatever appeared in the research prose.
/// </para>
/// </summary>
public static class GccRequiredToolMentions
{
    /// <summary>
    /// The partner tool names declared on this create's brief, in the operator's own order.
    /// Empty when the brief declares none, which is not an error -- it is a create with no partners.
    /// </summary>
    public static IReadOnlyList<string> For(string? briefJson, IReadOnlyList<string>? partnerUrls = null)
    {
        var names = GccPartnerUrlResearchService.CollectPartnerToolRows(briefJson)
            .Select(row => row.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Every partner URL on the project is a partner, whether or not the brief also names it.
        // Reading the brief alone made the requirement silently empty when the operator had entered
        // five partner sites and left the tool rows blank -- a check that passes because it is
        // asking for nothing is worse than no check, and is exactly how "two out of five" shipped
        // looking finished (Jeff, 2026-09-23: "The Tools mentioned don't reflect the Partners
        // entered ... I want, insist all five tools are mentioned").
        foreach (var derived in (partnerUrls ?? []).Select(NameFromUrl).Where(n => n.Length > 0))
        {
            // A brief row wins on spelling -- "HighRadius" beats a host-derived "Highradius" -- so
            // only add a derived name when nothing already covers that product.
            if (!names.Any(existing => Covers(existing, derived)))
            {
                names.Add(derived);
            }
        }

        return names;
    }

    /// <summary>Whether two names refer to the same product, ignoring spacing and punctuation.</summary>
    private static bool Covers(string a, string b)
    {
        static string Key(string v) => new([.. v.Where(char.IsLetterOrDigit)]);
        var left = Key(a);
        var right = Key(b);
        return left.Length > 0 && right.Length > 0
               && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
                   || right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A product name from a partner URL -- the registrable label of its host, capitalised.
    /// medius.com becomes Medius. A fallback, not the preferred source: it cannot know that
    /// "zoneandco" is written "Zone &amp; Co", which is why a brief tool row always wins.
    /// </summary>
    private static string NameFromUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        var label = host.Split('.').FirstOrDefault() ?? string.Empty;
        return label.Length == 0 ? string.Empty : char.ToUpperInvariant(label[0]) + label[1..];
    }

    /// <summary>
    /// The instruction block naming them, or null when there are none to name.
    /// </summary>
    public static string? Instruction(IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0)
        {
            return null;
        }

        var block = new StringBuilder()
            .AppendLine($"PARTNER TOOLS -- ALL {toolNames.Count} MUST BE NAMED (required):");
        foreach (var name in toolNames)
        {
            block.AppendLine($"  - {name}");
        }

        block.AppendLine(
            "Every one of these is named at least once in running prose, spelled exactly as written " +
            "above. These are the products this business promotes, so naming them is the point of " +
            "the page, not a decoration on it -- a draft that covers two of five has left three " +
            "partners out of a piece they are paying to appear in.");
        block.AppendLine(
            "Weave each into a sentence where it genuinely belongs: what it does for this reader, in " +
            "this context. Never a roundup section, never a product name as a heading, never a bare " +
            "list of names to satisfy the count. If the evidence supports saying more about one than " +
            "another, say more -- but every one gets named.");
        return block.ToString();
    }

    /// <summary>
    /// The names missing from a finished document, so the caller can refuse it. Empty means every
    /// required tool was named.
    /// </summary>
    public static IReadOnlyList<string> Missing(ContentDocument document, IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0)
        {
            return [];
        }

        var text = ContentDocumentText.Flatten(document);
        return [.. toolNames.Where(name => text.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)];
    }
}
