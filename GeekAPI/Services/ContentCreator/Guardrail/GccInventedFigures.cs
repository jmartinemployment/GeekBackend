using System.Text;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>A sentence taken out of a page because it stated a figure the writer was shown nowhere.</summary>
/// <param name="Heading">The heading of the section it was under.</param>
/// <param name="Figures">The figures in it that are in none of the page's evidence, as written.</param>
/// <param name="Sentence">The sentence, whole.</param>
public sealed record GccRemovedSentence(string Heading, IReadOnlyList<string> Figures, string Sentence);

/// <summary>
/// A sentence that states a figure the writer was shown nowhere is taken out of the page, and the
/// page is kept.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-13: "Stop the sentence, not the pillar or blog?" Until then one such figure refused
/// the whole page, and nothing is sent a second time. That day's 17:47 UTC run lost its pillar, 8,053
/// words, for one sentence: "If you sell 1,000 units of a digital service in Texas, but the tool
/// reads those as exempt, you could miss a nexus trigger entirely." The figure was the writer's own;
/// three partner pages in front of it gave Texas's threshold as $500,000 with no count of sales. The
/// sentence was wrong and had to go. The page did not.
/// </para>
/// <para>
/// <b>The same reading as the check.</b> What a figure is, and which figures the evidence holds, are
/// <see cref="GccFigureGrammar"/>'s, as <c>GccDraftGuard</c>'s <c>numbers</c> check reads them. This
/// runs before that check, so a page it has been through passes it, and what it could not take out
/// still refuses the page there.
/// </para>
/// <para>
/// <b>A heading left with nothing under it goes with its sentence,</b> when it is a heading under
/// another: a question of the page's FAQ whose whole answer was that sentence, a sub-section of one
/// line. It is named for the operator beside the sentence.
/// </para>
/// <para>
/// <b>What it cannot take out, and leaves for the check to refuse:</b> a figure in a block
/// quotation, which is the source's words and is not edited; and a sentence that is all the page's
/// opening, or all one of the page's own sections, has under it.
/// </para>
/// <para>
/// <b>Code deletes; it does not rewrite.</b> The sentences either side are left as written, so a
/// sentence that leaned on the one removed ("That is why...") may read oddly. Every sentence taken
/// out is named for the operator, with the heading it was under, so the place can be read.
/// </para>
/// </remarks>
internal static class GccInventedFigures
{
    /// <summary>What was taken out of a page.</summary>
    /// <param name="Document">The page without it. The page as it was when nothing was taken out.</param>
    /// <param name="Sentences">The sentences, in the order they stood.</param>
    /// <param name="Headings">The headings that went with them, each having nothing else under it.</param>
    public sealed record Result(
        ContentDocument Document, IReadOnlyList<GccRemovedSentence> Sentences, IReadOnlyList<string> Headings);

    /// <summary>
    /// <paramref name="document"/> without the sentences that state a figure
    /// <paramref name="numberEvidence"/> does not hold.
    /// </summary>
    public static Result Remove(ContentDocument document, string? numberEvidence)
    {
        var known = GccFigureGrammar.NumbersIn(numberEvidence);
        var sentences = new List<GccRemovedSentence>();
        var headings = new List<string>();
        // The opening and the page's own sections stay whatever happens under them: one left with
        // nothing is put back as written, for the check to refuse.
        var lede = Clean(document.Lede, known, sentences, headings, mayGo: false)!;
        var sections = document.Sections
            .Select(section => Clean(section, known, sentences, headings, mayGo: false)!)
            .ToList();
        return sentences.Count == 0
            ? new Result(document, sentences, headings)
            : new Result(document with { Lede = lede, Sections = sections }, sentences, headings);
    }

    /// <summary>
    /// What the operator is told when sentences were taken out of a page: how many, and each one with
    /// its figure and the heading it stood under. One line for a page's sentences together.
    /// </summary>
    /// <param name="type">"pillar", "blog" or "tool page".</param>
    public static string Line(string type, Result result)
    {
        static string One(GccRemovedSentence sentence) =>
            $"\"{sentence.Sentence}\" ({string.Join(", ", sentence.Figures)}), under \"{sentence.Heading}\"";

        var removed = result.Sentences;
        var line = removed.Count == 1
            ? $"The {type}: 1 sentence was deleted because it states a number that is in neither the Brief nor the "
              + $"research: {One(removed[0])}."
            : $"The {type}: {removed.Count} sentences were deleted because each states a number that is in neither the "
              + $"Brief nor the research: {string.Join("; ", removed.Select(One))}.";
        if (result.Headings.Count > 0)
        {
            var named = string.Join(", ", result.Headings.Select(heading => $"\"{heading}\""));
            line += result.Headings.Count == 1
                ? $" The heading {named} went with it, having nothing else under it."
                : $" The headings {named} went with them, having nothing else under them.";
        }

        return line + " The rest of the page is as written.";
    }

    /// <summary>
    /// <paramref name="section"/> without such sentences, or null when it is left with nothing under
    /// its heading and <paramref name="mayGo"/>: a heading over nothing is not left on a page.
    /// </summary>
    /// <param name="mayGo">True for a section under another. False for the opening and the page's own
    /// sections, which are put back as written when nothing would be left under them.</param>
    private static Section? Clean(
        Section section,
        IReadOnlySet<string> known,
        List<GccRemovedSentence> sentences,
        List<string> headings,
        bool mayGo)
    {
        var sentencesBefore = sentences.Count;
        var headingsBefore = headings.Count;
        var paragraphs = new List<Paragraph>();
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    var runs = Clean(text.Runs, known, section.Heading, sentences);
                    if (ReferenceEquals(runs, text.Runs)) paragraphs.Add(text);
                    else if (runs.Count > 0) paragraphs.Add(text with { Runs = runs });
                    break;

                case ListParagraph list:
                    var items = new List<IReadOnlyList<Run>>();
                    var changed = false;
                    foreach (var item in list.Items)
                    {
                        var kept = Clean(item, known, section.Heading, sentences);
                        if (ReferenceEquals(kept, item)) items.Add(item);
                        else
                        {
                            changed = true;
                            if (kept.Count > 0) items.Add(kept);
                        }
                    }

                    if (!changed) paragraphs.Add(list);
                    else if (items.Count > 0) paragraphs.Add(list with { Items = items });
                    break;

                // A quotation is the source's words, a code block is not prose, and a term with its
                // definition is not read for figures by the check either.
                default:
                    paragraphs.Add(paragraph);
                    break;
            }
        }

        var children = section.Children
            .Select(child => Clean(child, known, sentences, headings, mayGo: true))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToList();

        if (sentences.Count == sentencesBefore) return section;
        if (paragraphs.Count > 0 || children.Count > 0) return section with { Paragraphs = paragraphs, Children = children };

        if (mayGo)
        {
            headings.Add(section.Heading);
            return null;
        }

        sentences.RemoveRange(sentencesBefore, sentences.Count - sentencesBefore);
        headings.RemoveRange(headingsBefore, headings.Count - headingsBefore);
        return section;
    }

    /// <summary>
    /// One paragraph's or one list item's runs without the sentences that state a figure not in the
    /// evidence. The same list when there is none; an empty one when nothing is left.
    /// </summary>
    private static IReadOnlyList<Run> Clean(
        IReadOnlyList<Run> runs, IReadOnlySet<string> known, string heading, List<GccRemovedSentence> into)
    {
        // The text and the linked ranges exactly as the check joins them, so a year in a cite is no
        // figure here either.
        var joined = new StringBuilder();
        var cites = new List<(int Start, int End)>();
        foreach (var run in runs)
        {
            var start = joined.Length;
            joined.Append(run.Text);
            if (!string.IsNullOrWhiteSpace(run.Href)) cites.Add((start, joined.Length));
        }

        var text = joined.ToString();
        var invented = GccFigureGrammar.Find(text, cites).Where(figure => !known.Contains(figure.Value)).ToList();
        if (invented.Count == 0) return runs;

        var cut = new List<(int Start, int End)>();
        foreach (var sentence in SentencesOf(text))
        {
            var inIt = invented.Where(figure => figure.Index >= sentence.Start && figure.Index < sentence.End).ToList();
            if (inIt.Count == 0) continue;
            cut.Add(sentence);
            into.Add(new GccRemovedSentence(
                heading, [.. inIt.Select(figure => figure.Written)], text[sentence.Start..sentence.End].Trim()));
        }

        var kept = new List<Run>();
        var offset = 0;
        foreach (var run in runs)
        {
            var end = offset + run.Text.Length;
            var remaining = new StringBuilder();
            var at = offset;
            foreach (var (start, stop) in cut)
            {
                if (stop <= at || start >= end) continue;
                if (start > at) remaining.Append(text, at, start - at);
                at = Math.Min(stop, end);
            }

            if (at < end) remaining.Append(text, at, end - at);
            offset = end;
            if (remaining.Length > 0) kept.Add(run with { Text = remaining.ToString() });
        }

        // A sentence takes the space after it when it goes, so only the paragraph's end can be left
        // with one: when its last sentence was the one removed.
        while (kept.Count > 0 && kept[^1].Text.TrimEnd().Length == 0) kept.RemoveAt(kept.Count - 1);
        if (kept.Count > 0) kept[^1] = kept[^1] with { Text = kept[^1].Text.TrimEnd() };
        return kept;
    }

    /// <summary>Marks that close a sentence after its full stop: a quotation mark or a bracket.</summary>
    private const string Closers = "\"'”’)]";

    /// <summary>Marks a sentence can open with, besides a capital or a digit.</summary>
    private const string Openers = "\"'“‘([$€£";

    /// <summary>
    /// Words written with a full stop that does not end a sentence, without that stop. "No." and
    /// "St." are left out: each is far more often a whole sentence or its end.
    /// </summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g", "i.e", "etc", "vs", "inc", "corp", "ltd", "co", "mr", "mrs", "ms", "dr", "jr", "sr", "u.s", "a.m", "p.m",
    };

    /// <summary>
    /// The sentences of <paramref name="text"/> as ranges that cover all of it, each with the space
    /// after it. A sentence ends at a full stop, a question mark or an exclamation mark that is
    /// followed by space and then a capital, a digit or an opening mark, or by the end of the text.
    /// </summary>
    /// <remarks>
    /// "4.5" and "bill.com" have no space after the stop. "e.g. a late invoice" goes on in lower case.
    /// "the U.S. Supreme Court" and "J. Smith" are held together by <see cref="Abbreviations"/> and the
    /// single capital. Where this is unsure it keeps two sentences as one, which takes out more
    /// rather than leaving half a sentence behind.
    /// </remarks>
    internal static IReadOnlyList<(int Start, int End)> SentencesOf(string text)
    {
        var sentences = new List<(int Start, int End)>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] is not ('.' or '!' or '?'))
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < text.Length && text[end] is '.' or '!' or '?') end++;
            while (end < text.Length && Closers.Contains(text[end])) end++;
            var next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;

            var ends = next == text.Length
                || (next > end && StartsASentence(text[next]) && !(text[i] == '.' && IsAbbreviation(text, i)));
            if (ends)
            {
                sentences.Add((start, next));
                start = next;
            }

            i = Math.Max(next, i + 1);
        }

        if (start < text.Length) sentences.Add((start, text.Length));
        return sentences;
    }

    private static bool StartsASentence(char c) => char.IsUpper(c) || char.IsDigit(c) || Openers.Contains(c);

    /// <summary>Whether the full stop at <paramref name="stop"/> belongs to an abbreviation or an initial.</summary>
    private static bool IsAbbreviation(string text, int stop)
    {
        var from = stop;
        while (from > 0 && (char.IsLetter(text[from - 1]) || text[from - 1] == '.')) from--;
        var word = text[from..stop];
        return (word.Length == 1 && char.IsUpper(word[0])) || Abbreviations.Contains(word);
    }
}
