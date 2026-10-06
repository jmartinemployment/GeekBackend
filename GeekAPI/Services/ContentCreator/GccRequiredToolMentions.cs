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

    /// <summary>
    /// Host -> the spelling that host's product is written with, for a caller that has a link and
    /// needs the name.
    ///
    /// <para>
    /// Here rather than at the caller because the precedence is already decided here: a brief row
    /// beats a host-derived name, because a host cannot know that "zoneandco" is written "Zone &amp;
    /// Co". A caller that built this mapping itself would have to re-derive that rule, and the first
    /// time the two disagreed the prompt would name one partner two ways -- the required-mentions
    /// block asking for "Zone &amp; Co" while a retrieved chunk was labelled "Zoneandco".
    /// </para>
    ///
    /// <para>
    /// Keys are registrable hosts without a leading "www.", lowercased; lookups are
    /// case-insensitive. Empty when the create declares no partners, which is not an error.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> AnchorLookup(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls = null)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = For(briefJson, partnerUrls);
        if (names.Count == 0)
        {
            return lookup;
        }

        // A partner URL contributes its host; the name comes from the merged list, so a brief row's
        // spelling is what lands even when the key was derived from the URL.
        foreach (var url in partnerUrls ?? [])
        {
            var host = HostOf(url);
            if (host.Length == 0 || lookup.ContainsKey(host))
            {
                continue;
            }

            var derived = NameFromUrl(url);
            var authoritative = names.FirstOrDefault(name => Covers(name, derived)) ?? derived;
            if (authoritative.Length == 0)
            {
                continue;
            }

            lookup[host] = authoritative;
        }

        // Brief rows last, and they overwrite: a row carries both the URL and the operator's own
        // spelling, which is the most authoritative pairing available.
        foreach (var row in GccPartnerUrlResearchService.CollectPartnerToolRows(briefJson))
        {
            var host = HostOf(row.Url);
            if (host.Length == 0 || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            lookup[host] = row.Name.Trim();
        }

        return lookup;
    }

    /// <summary>
    /// Partner name to the vendor's own home page — the manufacturer's URL, not ours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For structured data. A <c>SoftwareApplication</c> node's <c>url</c> is where the product
    /// lives, and ours is <c>mainEntityOfPage</c>; publishing our page as the product's url told
    /// search engines "Tipalti is located at geekatyourspot.com" until 2026-09-23. That was fixed
    /// by passing null, because the vendor's domain was not on a GeneratedContent row — which it
    /// no longer has to be. The project declares its partner URLs, and those are the manufacturers'
    /// domains.
    /// </para>
    /// <para>
    /// Built here rather than at the caller for the reason <see cref="AnchorLookup"/> gives: the
    /// precedence between a brief row's spelling and a host-derived name is already decided here,
    /// and a caller that re-derived it would name one partner two ways the first time they
    /// disagreed.
    /// </para>
    /// <para>
    /// Keyed on the authoritative name, case-insensitively, so a caller holding a tool's name can
    /// ask directly. The value is the scheme and host only: a partner URL may be a deep link to one
    /// page, and a product's <c>url</c> is its home, not whichever page was declared.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> HomeUrlByName(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls = null)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = AnchorLookup(briefJson, partnerUrls);
        if (names.Count == 0)
        {
            return byName;
        }

        // One pass over the declared URLs, matched to the name AnchorLookup settled on for that
        // host. A host with no name, or a name already claimed, is skipped rather than guessed at:
        // attaching the wrong vendor's domain to a product is worse than the omission this
        // replaces.
        foreach (var url in partnerUrls ?? [])
        {
            var host = HostOf(url);
            if (host.Length == 0 || !names.TryGetValue(host, out var name)) continue;
            if (byName.ContainsKey(name)) continue;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) continue;

            byName[name] = $"{uri.Scheme}://{uri.Host}";
        }

        foreach (var row in GccPartnerUrlResearchService.CollectPartnerToolRows(briefJson))
        {
            if (string.IsNullOrWhiteSpace(row.Name)) continue;
            if (!Uri.TryCreate(row.Url?.Trim(), UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;

            // Brief rows overwrite, same as AnchorLookup: a row carries both the URL and the
            // operator's own spelling, which is the most authoritative pairing available.
            byName[row.Name.Trim()] = $"{uri.Scheme}://{uri.Host}";
        }

        return byName;
    }

    /// <summary>
    /// The registrable host of a URL, lowercased and without a leading "www.". Empty when the value
    /// is not an absolute http(s) URL, which is the only form a partner URL is stored in.
    /// </summary>
    /// <summary>
    /// The registrable host of a URL, lowercased and without a leading <c>www.</c> — the key this
    /// file's lookups are built on, and the one way to bucket anything by partner.
    /// </summary>
    /// <remarks>
    /// Public because the key has to be computed outside this class to be useful: a page can only be
    /// matched to the partner that <see cref="AnchorLookup"/> named by computing the same key the
    /// lookup was built with. It was private, and two copies of this normalization grew elsewhere.
    /// </remarks>
    public static string HostKeyOf(string? url) => HostOf(url);

    private static string HostOf(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return string.Empty;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>Whether two names refer to the same product, ignoring spacing and punctuation.</summary>
    public static bool SameProduct(string a, string b) => Covers(a, b);

    /// <summary>
    /// The instruction naming the tools this piece must not name, or null when there are none.
    /// </summary>
    /// <remarks>
    /// The publisher's own site lists tools that are not this project's partners, and the writer sees
    /// that site's headings and prose, so the names are in front of it. Naming them here is what stops
    /// "tools like ApprovalMax, Melio, and Ramp" on a project whose partners do not include Melio.
    /// </remarks>
    public static string? UnlistedInstruction(IReadOnlyList<string> unlisted)
    {
        if (unlisted.Count == 0) return null;

        return new StringBuilder()
            .AppendLine($"TOOLS THIS PIECE DOES NOT NAME: {string.Join(", ", unlisted)}.")
            .AppendLine(
                "This site covers them elsewhere, but they are not this project's partners. Do not name them and " +
                "do not link them, anywhere in this piece. The only tools this piece names are the partner " +
                "tools listed above.")
            .ToString();
    }

    /// <summary>The names in <paramref name="unlisted"/> that the document uses, as whole words.</summary>
    public static IReadOnlyList<string> Named(ContentDocument document, IReadOnlyList<string> unlisted)
    {
        if (unlisted.Count == 0) return [];

        var text = ContentDocumentText.Flatten(document);
        return [.. unlisted.Where(name => NamesAsWord(text, name))];
    }

    /// <summary>
    /// A sentence that names some of the partner tools and not the rest. By omission it says the rest
    /// cannot do what the sentence describes.
    /// </summary>
    /// <param name="Sentence">The sentence as written, trimmed.</param>
    /// <param name="Named">The partners it names, in the operator's order.</param>
    /// <param name="Unnamed">The partners it leaves out, in the operator's order.</param>
    public sealed record PartialPartnerList(string Sentence, IReadOnlyList<string> Named, IReadOnlyList<string> Unnamed);

    /// <summary>
    /// Every sentence that names two or more of the partner tools but not all of them. Empty when there
    /// is none, or when there are fewer than three partners, since then every sentence names one or all.
    /// </summary>
    /// <remarks>
    /// "Software like Lightyear, Ramp, and Bill offer powerful automation capabilities ... These tools
    /// integrate seamlessly with existing accounting systems" on a project with Stampli and Approvalmax
    /// as well. Jeff, 2026-10-06: "Which implies the other two do not?" It does. The mentions check saw
    /// all five named somewhere on the page and passed it; nothing looked at how they were grouped. One
    /// partner alone is a claim about that partner; all of them is a claim about the set; some of them
    /// is an exclusion nobody decided.
    ///
    /// A sentence is text between sentence-ending punctuation followed by white space, so "Bill.com
    /// offers" does not split. Names are matched as whole words, exactly as the brief spells them, the
    /// same way <see cref="Named"/> matches an unlisted tool.
    /// </remarks>
    public static IReadOnlyList<PartialPartnerList> PartialLists(ContentDocument document, IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count < 3) return [];

        var found = new List<PartialPartnerList>();
        foreach (var sentence in Sentences(document))
        {
            var named = toolNames.Where(name => NamesAsWord(sentence, name)).ToList();
            if (named.Count >= 2 && named.Count < toolNames.Count)
            {
                found.Add(new PartialPartnerList(sentence, named, [.. toolNames.Where(name => !named.Contains(name))]));
            }
        }

        return found;
    }

    private static IEnumerable<string> Sentences(ContentDocument document)
    {
        foreach (var section in AllSections(document))
        {
            foreach (var paragraph in ContentDocumentText.ParagraphTexts(section))
            {
                foreach (var sentence in System.Text.RegularExpressions.Regex.Split(paragraph, @"(?<=[.!?])\s+"))
                {
                    if (!string.IsNullOrWhiteSpace(sentence)) yield return sentence.Trim();
                }
            }
        }
    }

    private static IEnumerable<Section> AllSections(ContentDocument document)
    {
        yield return document.Lede;
        foreach (var section in Descend(document.Sections)) yield return section;

        static IEnumerable<Section> Descend(IReadOnlyList<Section> sections)
        {
            foreach (var section in sections)
            {
                yield return section;
                foreach (var child in Descend(section.Children)) yield return child;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> names <paramref name="name"/> as a whole word. A brand is itself
    /// whatever its capitals -- "ApprovalMax" is "Approvalmax" -- but a name that starts with a capital
    /// is not matched by the lower-case word: "a bill arrives" does not name Bill.
    /// </summary>
    /// <remarks>
    /// The 16:52 run of 2026-10-06 wrote "tools like Bill, Ramp, and ApprovalMax" against a partner
    /// list that spells it "Approvalmax", and the partial-list finding reported ApprovalMax as not
    /// named -- the finding named the wrong sides. One matcher for the unlisted-tool and partial-list
    /// checks, so the two cannot read a name differently.
    /// </remarks>
    internal static bool NamesAsWord(string text, string name)
    {
        var pattern = $@"(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(name)}(?![A-Za-z0-9])";
        var capitalised = name.Length > 0 && char.IsUpper(name[0]);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            if (!capitalised || char.IsUpper(match.Value[0])) return true;
        }

        return false;
    }

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
            "Find the evidence for each name in QUOTEABLE RESEARCH. A passage headed \"Target Entity " +
            "Match: <name>\" is evidence about that tool specifically, established from the links in " +
            "the passage itself, and the spelling in that label is the spelling listed above -- the " +
            "same source produced both. A tool with a labelled passage therefore has something " +
            "sourceable to say about it: say it, and cite that passage. A tool with no labelled " +
            "passage is named plainly for what it is, with no claims attached.");
        block.AppendLine(
            "Weave each into a sentence where it genuinely belongs: what it does for this reader, in " +
            "this context. Never a roundup section, never a product name as a heading, never a bare " +
            "list of names to satisfy the count. If the evidence supports saying more about one than " +
            "another, say more -- but every one gets named.");
        block.AppendLine(
            "Name them all together, or one at a time -- never some of them. A sentence that names some " +
            "of these tools and not the rest tells the reader the rest cannot do what it describes. Where " +
            "a capability is shared, name every one of them in that sentence or name none by name; where " +
            "it is one tool's, name that tool alone, with the evidence for it.");
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
