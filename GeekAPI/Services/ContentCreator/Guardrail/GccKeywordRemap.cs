using System.Text.RegularExpressions;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Turns the writer's own shortenings of the keyword back into the exact phrase, where the grammar
/// allows it, until the page carries its count, spread over its sections.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-10: <i>"Do not rely on the LLM to count its own keyword usage to hit a target of 6 or
/// 7 mentions. Let the LLM write naturally, and write a light post-processing utility that replaces
/// natural semantic variations with the strict required target phrase where grammatically
/// appropriate."</i> The run of 2026-10-07 is why: told to use "Automated Approval Workflows" six
/// times a call, the writer used it twice and wrote "approval workflows" fifty-five times and
/// "automated workflows" seven. A changed hyphen or spacing, never. The variation is a dropped word.
/// </para>
/// <para>
/// <b>A variation is the keyword with one word left out, never its last.</b> For a keyword of three
/// or more words, each word but the last may be the one dropped: "approval workflows" and "automated
/// workflows" for "Automated Approval Workflows". The last word is the noun the phrase is about, and
/// "automated approval" is a different noun, so it is never extended. A two-word keyword has no
/// variation, and a synonym is not one: "AR automation" is not "automated accounts receivable" with
/// a word missing, and nothing here guesses that it means the same.
/// </para>
/// <para>
/// <b>Where the grammar allows it.</b> A word put back in the middle of its own phrase reads as the
/// phrase ("automated workflows" is "automated approval workflows"). A word put back at the front
/// takes the slot before the phrase, so it is put back only when that slot is open: the start of a
/// sentence or clause, or a word that does not describe the phrase -- an article, a preposition, a
/// conjunction, a pronoun, an auxiliary (<see cref="FunctionWords"/>). After an adjective or a verb
/// the slot is taken: "manual approval workflows" and "automating approval workflows" are left as
/// written, because "manual automated approval workflows" and "automating automated approval
/// workflows" are wrong. On the 2026-10-07 pages this rule reaches 20 of the 54 variations.
/// </para>
/// <para>
/// <b>How many.</b> The page's count is <see cref="TargetFor"/>: the same 0.6% the prompt used to
/// state, taken of the words the page has rather than of its floor. It is spread across the opening
/// and the body sections, each section's share counted the way the scorer counts
/// (<see cref="GcwSeoAnalyzer.CountPhraseOccurrences"/>, headings included). A section under its
/// share takes what it can of it. Within a section the edits are spread over the paragraphs that
/// offer one, never two in a paragraph and never in a paragraph that already carries the phrase. A
/// section with more exact uses than its share keeps them: nothing is ever removed.
/// </para>
/// <para>
/// <b>A share a section cannot take goes to the sections that can.</b> A section may have no
/// paragraph that offers an edit: every shortening in it follows an adjective, or it has none. Until
/// 2026-10-10 its share was lost, and the page stopped short of its own count while other sections
/// had paragraphs to spare. The run of that day shows it: four tool pages brought to 16 of 20, 13 of
/// 19, 14 of 19 and 15 of 21, each two uses under the score's floor, each with six to sixteen
/// paragraphs still saying "accounts receivable" without "automated". What the page is still short
/// of once every section has taken its own is now handed out one use at a time, to the section with
/// the fewest uses that still has a paragraph to offer, the earlier one when two are level. The
/// limits are the same: one edit a paragraph, and never past the page's count.
/// </para>
/// <para>
/// <b>What is never touched.</b> Headings: the keyword's heading is the writer's own job, held by the
/// batch check. A quotation: its words are the source's and are verified against it. A run with a
/// link, or any formatting: the closing is built by code and the linker has not run yet when this
/// does, so neither occurs on a draft, and either is left alone if it does. A code block, a term and
/// a definition are not prose. Nothing here refuses, and the page's keyword count is judged once,
/// afterwards, by the page's own score (<see cref="GccDraftGuard"/>).
/// </para>
/// </remarks>
public static class GccKeywordRemap
{
    /// <summary>The share of a page's words the exact phrase is brought up to: 0.6%, well inside the scorer's 0.4% to 2.5%.</summary>
    public const double ShareOfWords = 0.006;

    /// <summary>Fewer than this is a page with no keyword presence at all, whatever its length.</summary>
    public const int FewestUses = 4;

    /// <summary>One edit made: the heading it sits under, the words as the writer had them, and the words now.</summary>
    public sealed record Edit(string Heading, string From, string To);

    /// <summary>
    /// One section's part in the count, the opening first: its share of the page's count, the exact
    /// uses it had, the paragraphs that offered an edit, and the edits made in it. A section with no
    /// place offers none, and what it could not take shows as edits above another section's share.
    /// </summary>
    public sealed record SectionUse(string Heading, int Share, int Before, int Places, int Edits);

    /// <summary>
    /// The document with its edits made, the count it was brought toward, the counts before and after,
    /// and each section's part in them.
    /// </summary>
    public sealed record Remapped(
        ContentDocument Document,
        int Target,
        int Before,
        int After,
        IReadOnlyList<Edit> Edits,
        IReadOnlyList<SectionUse> Sections);

    /// <summary>
    /// Words that can stand immediately before the phrase without describing it, so the keyword's first
    /// word can be put back between them and the rest of the phrase.
    /// </summary>
    internal static readonly IReadOnlySet<string> FunctionWords = new HashSet<string>(StringComparer.Ordinal)
    {
        // Determiners and pronouns.
        "the", "a", "an", "this", "that", "these", "those", "my", "our", "your", "their", "its", "his",
        "her", "any", "some", "all", "both", "each", "every", "no", "such", "another", "other", "most",
        "many", "more", "few", "several", "which", "what", "whose",
        // Prepositions.
        "of", "in", "on", "at", "for", "with", "to", "into", "onto", "from", "by", "about", "through",
        "across", "without", "within", "over", "under", "around", "between", "among", "against",
        "after", "before", "during", "toward", "towards", "upon", "via", "like", "versus", "vs", "than",
        "as", "per", "behind", "beyond", "beside", "despite", "until", "till", "since", "throughout",
        "inside", "outside", "up", "down", "off", "out",
        // Conjunctions.
        "and", "or", "but", "nor", "so", "yet", "if", "when", "where", "while", "because", "although",
        "though", "unless", "whereas", "whether",
        // Auxiliaries and the copula.
        "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "will", "would", "can", "could", "should", "may", "might", "must", "shall",
        // Adverbs that sit before a noun phrase.
        "also", "still", "then", "now", "already", "even", "just", "only", "not", "especially",
        "particularly", "including", "mostly", "often", "usually", "typically", "generally",
    };

    /// <summary>The count a page of <paramref name="words"/> words is brought toward.</summary>
    public static int TargetFor(int words) => Math.Max(FewestUses, (int)Math.Round(words * ShareOfWords));

    /// <summary>
    /// A total spread over <paramref name="sections"/> so the shares add up to it exactly and no two
    /// differ by more than one: the arithmetic <c>SectionSlot.WithOwedWords</c> uses for words.
    /// </summary>
    internal static IReadOnlyList<int> Shares(int total, int sections)
    {
        if (sections <= 0) return [];
        var shares = new int[sections];
        for (var i = 0; i < sections; i++)
        {
            var throughThisOne = (int)((long)(i + 1) * total / sections);
            var beforeThisOne = (int)((long)i * total / sections);
            shares[i] = throughThisOne - beforeThisOne;
        }

        return shares;
    }

    public static Remapped Apply(ContentDocument document, string? keyword)
    {
        var phrase = (keyword ?? string.Empty).Trim();
        var before = CountIn(document.Lede, phrase) + document.Sections.Sum(s => CountIn(s, phrase));
        var target = TargetFor(ContentDocumentText.CountWords(document));
        var variations = VariationsOf(phrase);
        if (phrase.Length == 0 || variations.Count == 0)
        {
            return new Remapped(document, phrase.Length == 0 ? 0 : target, before, before, [], []);
        }

        var all = new List<Section>(document.Sections.Count + 1) { document.Lede };
        all.AddRange(document.Sections);
        var shares = Shares(target, all.Count);

        // What each section has, how many of its paragraphs offer an edit, and what it takes of its
        // own share.
        var have = new int[all.Count];
        var places = new int[all.Count];
        var taken = new int[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            have[i] = CountIn(all[i], phrase);
            var offered = 0;
            Walk(all[i], phrase, variations, chosen: null, ref offered, edits: null);
            places[i] = offered;
            taken[i] = Math.Min(Math.Max(0, shares[i] - have[i]), offered);
        }

        // What the page is still short of is what some section could not take of its share. It goes,
        // a use at a time, to the section with the fewest uses that still has a place.
        for (var left = target - before - taken.Sum(); left > 0; left--)
        {
            var next = -1;
            for (var i = 0; i < all.Count; i++)
            {
                if (taken[i] >= places[i]) continue;
                if (next < 0 || have[i] + taken[i] < have[next] + taken[next]) next = i;
            }

            if (next < 0) break;
            taken[next]++;
        }

        var edits = new List<Edit>();
        var result = new List<Section>(all.Count);
        var uses = new List<SectionUse>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            uses.Add(new SectionUse(all[i].Heading, shares[i], have[i], places[i], taken[i]));
            if (taken[i] == 0)
            {
                result.Add(all[i]);
                continue;
            }

            // The paragraphs taken, spread over the ones that offer an edit, in reading order.
            var chosen = new HashSet<int>();
            for (var j = 0; j < taken[i]; j++) chosen.Add((int)((long)j * places[i] / taken[i]));
            var ordinal = 0;
            result.Add(Walk(all[i], phrase, variations, chosen, ref ordinal, edits));
        }

        var remapped = new ContentDocument(result[0], result.Skip(1).ToList());
        var after = CountIn(remapped.Lede, phrase) + remapped.Sections.Sum(s => CountIn(s, phrase));
        return new Remapped(remapped, target, before, after, edits, uses);
    }

    /// <summary>
    /// The keyword with one word left out, for each word but the last: the pattern that finds it as
    /// whole words, the word to put back, and where.
    /// </summary>
    internal sealed record Variation(Regex Pattern, string[] Words, int DroppedAt);

    internal static IReadOnlyList<Variation> VariationsOf(string phrase)
    {
        var words = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return [];

        var variations = new List<Variation>(words.Length - 1);
        for (var dropped = 0; dropped < words.Length - 1; dropped++)
        {
            var kept = words.Where((_, k) => k != dropped).Select(WordPattern);
            var pattern = new Regex(
                @"(?<![A-Za-z0-9])" + string.Join(@"\s+", kept) + @"(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            variations.Add(new Variation(pattern, words, dropped));
        }

        return variations;
    }

    /// <summary>"&amp;" and "and" are the same word, as the scorer counts them.</summary>
    private static string WordPattern(string word) =>
        word == "&" || string.Equals(word, "and", StringComparison.OrdinalIgnoreCase)
            ? "(?:&|and)"
            : Regex.Escape(word);

    /// <summary>The section's exact uses, counted as the scorer counts them: its headings and every run, joined a part to a line.</summary>
    internal static int CountIn(Section section, string phrase)
    {
        if (phrase.Length == 0) return 0;
        var parts = new List<string>();
        Collect(section, parts);
        return GcwSeoAnalyzer.CountPhraseOccurrences(string.Join("\n", parts), phrase);
    }

    private static void Collect(Section section, List<string> parts)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading)) parts.Add(section.Heading);
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    parts.AddRange(text.Runs.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    break;
                case ListParagraph list:
                    foreach (var item in list.Items)
                        parts.AddRange(item.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    break;
                case QuoteParagraph quote:
                    parts.AddRange(quote.Runs.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    break;
                default:
                    break;
            }
        }

        foreach (var child in section.Children) Collect(child, parts);
    }

    /// <summary>
    /// One pass over a section's paragraphs in reading order. With no <paramref name="chosen"/> set it
    /// counts the paragraphs that offer an edit; with one it makes the edits whose ordinal is chosen.
    /// The two passes walk the same order, so an ordinal names the same paragraph in both.
    /// </summary>
    private static Section Walk(
        Section section,
        string phrase,
        IReadOnlyList<Variation> variations,
        HashSet<int>? chosen,
        ref int ordinal,
        List<Edit>? edits)
    {
        var paragraphs = new List<Paragraph>(section.Paragraphs.Count);
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    paragraphs.Add(Offer(text.Runs, phrase, variations, section.Heading, chosen, ref ordinal, edits) is { } runs
                        ? new TextParagraph(runs)
                        : text);
                    break;

                case ListParagraph list:
                {
                    var items = new List<IReadOnlyList<Run>>(list.Items.Count);
                    foreach (var item in list.Items)
                    {
                        items.Add(Offer(item, phrase, variations, section.Heading, chosen, ref ordinal, edits) ?? item);
                    }

                    paragraphs.Add(new ListParagraph(list.Ordered, items));
                    break;
                }

                default:
                    paragraphs.Add(paragraph);
                    break;
            }
        }

        var children = new List<Section>(section.Children.Count);
        foreach (var child in section.Children)
        {
            children.Add(Walk(child, phrase, variations, chosen, ref ordinal, edits));
        }

        return section with { Paragraphs = paragraphs, Children = children };
    }

    /// <summary>
    /// The runs of one paragraph with its edit made, or null when it offers none or was not chosen.
    /// The paragraph's runs are plain text or it is left alone; its text is read as one string, so a
    /// variation split across two runs is found, and it is written back as one run.
    /// </summary>
    private static IReadOnlyList<Run>? Offer(
        IReadOnlyList<Run> runs,
        string phrase,
        IReadOnlyList<Variation> variations,
        string heading,
        HashSet<int>? chosen,
        ref int ordinal,
        List<Edit>? edits)
    {
        if (runs.Count == 0 || runs.Any(r => r.Bold || r.Italic || !string.IsNullOrWhiteSpace(r.Href))) return null;

        var text = string.Concat(runs.Select(r => r.Text ?? string.Empty));
        if (GcwSeoAnalyzer.CountPhraseOccurrences(text, phrase) > 0) return null;

        if (FirstAllowed(text, variations) is not { } found) return null;

        var mine = ordinal++;
        if (chosen is null || !chosen.Contains(mine)) return null;

        var (from, to) = Rewrite(text, found.Match, found.Variation);
        edits?.Add(new Edit(heading, from, to));
        var edited = text[..found.Match.Index] + to + text[(found.Match.Index + found.Match.Length)..];
        return [runs[0] with { Text = edited }];
    }

    /// <summary>The earliest occurrence of any variation whose slot is open.</summary>
    private static (Match Match, Variation Variation)? FirstAllowed(string text, IReadOnlyList<Variation> variations)
    {
        (Match Match, Variation Variation)? first = null;
        foreach (var variation in variations)
        {
            // A variation's matches come in reading order: the first with an open slot is its best, and
            // once that is past the best so far there is nothing earlier left to find.
            foreach (Match match in variation.Pattern.Matches(text))
            {
                if (first is { } earlier && earlier.Match.Index <= match.Index) break;
                if (SlotIsOpen(text, match, variation))
                {
                    first = (match, variation);
                    break;
                }
            }
        }

        return first;
    }

    /// <summary>
    /// Whether the dropped word can go back. In the middle of the phrase it always can. At the front,
    /// only when what precedes the match is a sentence or clause boundary or a function word -- and
    /// never the keyword's own first word, which makes this an exact use, not a variation.
    /// </summary>
    private static bool SlotIsOpen(string text, Match match, Variation variation)
    {
        if (variation.DroppedAt > 0) return true;

        var before = text[..match.Index].TrimEnd();
        if (before.Length == 0) return true;
        var last = before[^1];
        if (!char.IsLetterOrDigit(last)) return true;

        var start = before.Length;
        while (start > 0 && (char.IsLetterOrDigit(before[start - 1]) || before[start - 1] is '\'' or '’' or '-')) start--;
        var token = before[start..].ToLowerInvariant();
        if (string.Equals(token, variation.Words[0], StringComparison.OrdinalIgnoreCase)) return false;
        return FunctionWords.Contains(token);
    }

    /// <summary>
    /// The matched words with the dropped one put back, in the case of the words around it: a
    /// title-cased phrase takes a title-cased word, a lower-cased one a lower-cased word, and a word
    /// put at the front of a sentence takes the capital and hands the old first word its lower case.
    /// A keyword word with a capital of its own ("QuickBooks", "AP") keeps it wherever it goes.
    /// </summary>
    private static (string From, string To) Rewrite(string text, Match match, Variation variation)
    {
        var from = match.Value;
        var matched = Regex.Split(from, @"\s+");
        var dropped = variation.Words[variation.DroppedAt];
        var intrinsic = dropped.Skip(1).Any(char.IsUpper);

        if (variation.DroppedAt == 0)
        {
            // Where the match sits is read off the text, not off the case of its first word: "QuickBooks"
            // is capitalised wherever it stands, and is no evidence of a sentence starting.
            var before = text[..match.Index].TrimEnd();
            var atSentenceStart = before.Length == 0
                || before[^1] is '.' or '!' or '?' or ':' or ';' or '"' or '“' or '‘' or '\'' or '(';
            // The matched words are the keyword's words from the second on; the ones with no capital of
            // their own say how the writer cased the phrase. An ampersand says nothing.
            var plain = new List<string>();
            for (var k = 0; k < matched.Length; k++)
            {
                if (!KeywordWordIsIntrinsic(variation.Words[k + 1]) && matched[k].Length > 0 && char.IsLetter(matched[k][0]))
                {
                    plain.Add(matched[k]);
                }
            }

            var titleCase = plain.Count > 0 && plain.All(w => w.Length > 0 && char.IsUpper(w[0]));
            var word = intrinsic
                ? dropped
                : atSentenceStart || titleCase ? Capitalized(dropped) : dropped.ToLowerInvariant();
            if (atSentenceStart && !titleCase && !KeywordWordIsIntrinsic(variation.Words[1]))
            {
                matched[0] = matched[0].ToLowerInvariant();
            }

            return (from, word + " " + string.Join(" ", matched));
        }

        var left = matched[variation.DroppedAt - 1];
        var right = matched[variation.DroppedAt];
        var titled = left.Length > 0 && char.IsUpper(left[0]) && right.Length > 0 && char.IsUpper(right[0]);
        var middle = intrinsic ? dropped : (titled ? Capitalized(dropped) : dropped.ToLowerInvariant());
        var rebuilt = new List<string>(matched.Length + 1);
        rebuilt.AddRange(matched.Take(variation.DroppedAt));
        rebuilt.Add(middle);
        rebuilt.AddRange(matched.Skip(variation.DroppedAt));
        return (from, string.Join(" ", rebuilt));
    }

    private static bool KeywordWordIsIntrinsic(string word) => word.Skip(1).Any(char.IsUpper);

    private static string Capitalized(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
