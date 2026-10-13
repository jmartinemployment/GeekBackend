using System.Reflection;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// No em dash in anything a writer produces: the rule the writer is given, and the code that makes it
/// true of what is saved.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-13: "This is a dead give away that writing is from AI, '—'. Can we put in a rule not
/// to use '—'?" Until then no request said so, the shared voice instructions used the character
/// themselves, and nothing read a draft for it.
/// </para>
/// <para>
/// <b>Two halves, because an instruction is not a guarantee.</b> Every writer is told
/// (<see cref="Instruction"/>, in the shared voice every channel prints). What a reply carries anyway
/// is taken out before the page is checked or saved (<see cref="Clean(ContentDocument)"/>, and
/// <see cref="Strings{T}"/> for a title, a meta description and a short piece), and counted on the
/// run's record.
/// </para>
/// <para>
/// <b>A comma takes its place.</b> A dash stands for a comma, a colon, a bracket or a full stop, and
/// code cannot tell which the sentence wanted; a comma is the one that is seldom wrong. Next to a
/// mark that is already there, the dash is simply dropped. This is the same kind of step as
/// <see cref="ContentGuardrail"/>, which rewrites a filler phrase: sanitising a reply, not writing one.
/// </para>
/// <para>
/// <b>Never in a quotation or in code.</b> A quotation is the source's words and is verified against
/// them; code is not prose. A section's provenance tag is the source's text copied exactly and is not
/// read here at all.
/// </para>
/// </remarks>
public static partial class GccEmDash
{
    /// <summary>The character: U+2014.</summary>
    public const char Dash = '—';

    /// <summary>What every writer is told. Printed once, in the voice every channel shares.</summary>
    public const string Instruction =
        "Never use the em dash (—). Do not put an en dash or a double hyphen in its place either. Where one "
        + "would go, use a comma, a colon, parentheses or a new sentence.";

    [GeneratedRegex(@"([.,;:!?(])[ \t]*—[ \t]*")]
    private static partial Regex AfterAMark();

    [GeneratedRegex(@"[ \t]*—[ \t]*([.,;:!?)])")]
    private static partial Regex BeforeAMark();

    [GeneratedRegex(@"[ \t]*—[ \t]*")]
    private static partial Regex Anywhere();

    /// <summary>
    /// <paramref name="text"/> with no em dash, and how many it held, for a text that stands alone (a
    /// title, a meta description, a heading, a line of an email). A comma and a space take the place
    /// of one between words; one beside another mark is dropped; one that opens the text is dropped
    /// with the space after it.
    /// </summary>
    public static (string Text, int Removed) Clean(string? text) => Clean(text, opensItsLine: true);

    /// <param name="opensItsLine">False for a run that follows another in its paragraph: a dash that
    /// opens such a run joins it to the words before, so the comma stays and so does the run's own
    /// leading space.</param>
    private static (string Text, int Removed) Clean(string? text, bool opensItsLine)
    {
        if (string.IsNullOrEmpty(text)) return (text ?? string.Empty, 0);
        var removed = text.Count(c => c == Dash);
        if (removed == 0) return (text, 0);

        var opensWithOne = opensItsLine && text.TrimStart().StartsWith(Dash);
        var working = AfterAMark().Replace(text, match => match.Groups[1].Value == "(" ? "(" : $"{match.Groups[1].Value} ");
        working = BeforeAMark().Replace(working, "$1");
        working = Anywhere().Replace(working, ", ");
        if (opensWithOne) working = working.TrimStart().TrimStart(',').TrimStart();
        return (working, removed);
    }

    /// <summary>
    /// <paramref name="document"/> with no em dash in a heading, a paragraph, a list item, a term or a
    /// definition, and how many were taken out. A quotation and a code block are returned as they are.
    /// </summary>
    public static (ContentDocument Document, int Removed) Clean(ContentDocument document)
    {
        var removed = 0;
        var lede = CleanSection(document.Lede, ref removed);
        var sections = new List<Section>(document.Sections.Count);
        foreach (var section in document.Sections) sections.Add(CleanSection(section, ref removed));
        return (removed == 0 ? document : new ContentDocument(lede, sections), removed);
    }

    /// <summary>
    /// A reply's own record (a title and meta description, a tool page's summaries, an email, a post)
    /// with no em dash in any string it holds, or in any list of strings, and how many were taken out.
    /// Every string the type declares, found by reflection, so a field added later is covered without
    /// a line here.
    /// </summary>
    public static (T Record, int Removed) Strings<T>(T record) where T : class
    {
        var removed = 0;
        var changes = new List<(PropertyInfo Property, object Value)>();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            var value = property.GetValue(record);
            if (value is string text)
            {
                var (cleaned, count) = Clean(text);
                if (count == 0) continue;
                removed += count;
                changes.Add((property, cleaned));
            }
            else if (value is List<string> list)
            {
                var count = 0;
                var cleanedList = new List<string>(list.Count);
                foreach (var item in list)
                {
                    var (cleaned, itemCount) = Clean(item);
                    count += itemCount;
                    cleanedList.Add(cleaned);
                }

                if (count == 0) continue;
                removed += count;
                changes.Add((property, cleanedList));
            }
        }

        if (removed == 0) return (record, 0);

        // A record's own copy, as `with` makes one, so the reply that was parsed is left as it came.
        var clone = typeof(T).GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)?.Invoke(record, null) as T;
        if (clone is null)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} is not a record, so its strings cannot be read for an em dash. Make it a record.");
        }

        foreach (var (property, value) in changes) property.SetValue(clone, value);
        return (clone, removed);
    }

    private static Section CleanSection(Section section, ref int removed)
    {
        var heading = section.Heading;
        if (!string.IsNullOrEmpty(heading))
        {
            var (cleaned, count) = Clean(heading);
            heading = cleaned;
            removed += count;
        }

        var paragraphs = new List<Paragraph>(section.Paragraphs.Count);
        foreach (var paragraph in section.Paragraphs) paragraphs.Add(CleanParagraph(paragraph, ref removed));
        var children = new List<Section>(section.Children.Count);
        foreach (var child in section.Children) children.Add(CleanSection(child, ref removed));
        return section with { Heading = heading, Paragraphs = paragraphs, Children = children };
    }

    private static Paragraph CleanParagraph(Paragraph paragraph, ref int removed)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                return new TextParagraph(CleanRuns(text.Runs, ref removed));
            case ListParagraph list:
            {
                var items = new List<IReadOnlyList<Run>>(list.Items.Count);
                foreach (var item in list.Items) items.Add(CleanRuns(item, ref removed));
                return new ListParagraph(list.Ordered, items);
            }
            case DefinitionParagraph definitions:
            {
                var items = new List<DefinitionItem>(definitions.Items.Count);
                foreach (var item in definitions.Items)
                {
                    items.Add(new DefinitionItem(CleanRuns(item.Term, ref removed), CleanRuns(item.Definition, ref removed)));
                }

                return new DefinitionParagraph(items);
            }
            default:
                // A quotation is the source's words; code is not prose. Both as they are.
                return paragraph;
        }
    }

    private static IReadOnlyList<Run> CleanRuns(IReadOnlyList<Run> runs, ref int removed)
    {
        var cleaned = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            var (text, count) = Clean(run.Text, opensItsLine: cleaned.Count == 0);
            removed += count;
            cleaned.Add(count == 0 ? run : run with { Text = text });
        }

        return cleaned;
    }
}
