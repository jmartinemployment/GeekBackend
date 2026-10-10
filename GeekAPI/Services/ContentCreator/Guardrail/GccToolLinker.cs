using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Puts each partner tool's page on the first mention of its name in the body. The writer links
/// nothing (Jeff, 2026-10-10): every shape that let it decide -- an href it typed, an anchor list it
/// copied, a target id it marked -- failed on a real run, and each failure cost a page. A link is a
/// deterministic operation on clean text, so code does it: one link per tool per page, on the tool's
/// own name and nothing around it, found by the one matcher every check reads
/// (<see cref="GccRequiredToolMentions.FindAsWord"/>).
/// </summary>
/// <remarks>
/// <para>
/// Called on the body sections alone, before the closing and the FAQ are appended, so the opening, the
/// operator's closing questions and the FAQ answers can never carry a tool link. Headings are strings
/// and are never linked; a quotation's words are the source's and are never linked; a run that already
/// carries an href is left alone.
/// </para>
/// <para>
/// Nothing here refuses. A tool the body never names is reported by name (it is also the
/// <c>partner-mentions</c> gap the guard reports); a tool with no public path is reported too. The text
/// of every paragraph, concatenated, is unchanged: a run is split, never rewritten.
/// </para>
/// </remarks>
public static class GccToolLinker
{
    /// <summary>One link placed: the tool, where it leads, the heading it sits under and the words it sits on.</summary>
    public sealed record ToolLink(string Tool, string Href, string Heading, string Words);

    /// <summary>The body with its tool links placed, every link placed, and every tool not linked with why.</summary>
    public sealed record Linked(List<Section> Sections, IReadOnlyList<ToolLink> Links, IReadOnlyList<string> NotLinked);

    public static Linked Link(IReadOnlyList<Section> sections, IReadOnlyList<KnownCrawlTool> tools)
    {
        var links = new List<ToolLink>();
        var notLinked = new List<string>();
        var result = sections.ToList();

        // Longest name first, so a name that contains another ("Bill.com" and "Bill") is linked whole.
        var named = tools
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .OrderByDescending(t => t.Name.Trim().Length)
            .ToList();
        foreach (var tool in named)
        {
            var name = tool.Name.Trim();
            var href = tool.PublicPath?.Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                notLinked.Add($"{name}: no public path for its tool page");
                continue;
            }

            var placed = false;
            for (var i = 0; i < result.Count && !placed; i++)
            {
                result[i] = LinkSection(result[i], name, href, links, ref placed);
            }

            if (!placed) notLinked.Add($"{name}: not named in the body");
        }

        return new Linked(result, links, notLinked);
    }

    private static Section LinkSection(Section section, string name, string href, List<ToolLink> links, ref bool placed)
    {
        var paragraphs = new List<Paragraph>(section.Paragraphs.Count);
        foreach (var paragraph in section.Paragraphs)
        {
            paragraphs.Add(placed ? paragraph : LinkParagraph(paragraph, section.Heading, name, href, links, ref placed));
        }

        var children = new List<Section>(section.Children.Count);
        foreach (var child in section.Children)
        {
            children.Add(placed ? child : LinkSection(child, name, href, links, ref placed));
        }

        return section with { Paragraphs = paragraphs, Children = children };
    }

    private static Paragraph LinkParagraph(
        Paragraph paragraph, string heading, string name, string href, List<ToolLink> links, ref bool placed)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                return new TextParagraph(LinkRuns(text.Runs, heading, name, href, links, ref placed));

            case ListParagraph list:
            {
                var items = new List<IReadOnlyList<Run>>(list.Items.Count);
                foreach (var item in list.Items)
                {
                    items.Add(placed ? item : LinkRuns(item, heading, name, href, links, ref placed));
                }

                return new ListParagraph(list.Ordered, items);
            }

            default:
                // A quotation's words are the source's; a code block, a term and a definition are not prose.
                return paragraph;
        }
    }

    private static IReadOnlyList<Run> LinkRuns(
        IReadOnlyList<Run> runs, string heading, string name, string href, List<ToolLink> links, ref bool placed)
    {
        var result = new List<Run>(runs.Count + 2);
        foreach (var run in runs)
        {
            if (placed || !string.IsNullOrWhiteSpace(run.Href))
            {
                result.Add(run);
                continue;
            }

            var text = run.Text ?? string.Empty;
            if (GccRequiredToolMentions.FindAsWord(text, name) is not { } at)
            {
                result.Add(run);
                continue;
            }

            var before = text[..at.Index];
            var words = text.Substring(at.Index, at.Length);
            var after = text[(at.Index + at.Length)..];
            if (before.Length > 0) result.Add(run with { Text = before });
            result.Add(run with { Text = words, Href = href });
            if (after.Length > 0) result.Add(run with { Text = after });
            links.Add(new ToolLink(name, href, heading, words));
            placed = true;
        }

        return result;
    }
}
