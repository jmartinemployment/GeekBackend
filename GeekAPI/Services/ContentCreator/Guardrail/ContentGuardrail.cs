using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Part 3 — deterministic content guardrail (Pass 1). Strips/replaces AI-filler and
/// corporate-jargon phrases from generated body text using a \b-bounded, case-insensitive
/// dictionary. Walks the structured <see cref="ContentDocument"/> and rewrites only
/// <see cref="Run.Text"/>, so markup/structure is preserved.
///
/// The rule set is seeded statically here. A DB-backed, admin-editable
/// <c>content_guardrail_rules</c> table + LLM restructure (Pass 2) are the planned next
/// increment (see docs/plans/content-creator-proposed-changes.plan.md Part 3.1 / 3.3 / 3.5);
/// this class is the single seam they will plug into (swap <see cref="Rules"/> for a repo).
/// </summary>
public static class ContentGuardrail
{
    public enum ActionType { Strip, Replace, Restructure }

    public sealed record Rule(string BannedPhrase, ActionType Action, string? Replacement, string ReasonCode);

    /// <summary>Representative seed (mirrors the plan's Part 3.1 table).</summary>
    public static readonly IReadOnlyList<Rule> Rules =
    [
        new("in today's fast-paced digital world", ActionType.Strip, null, "AI_FILLER"),
        new("delve deeper", ActionType.Replace, "examine", "AI_FILLER"),
        new("it is crucial to remember", ActionType.Strip, null, "AI_FILLER"),
        new("testament to", ActionType.Restructure, null, "AI_FILLER"),
        new("synergistic approach", ActionType.Replace, "collaborative strategy", "CORP_JARGON"),
        new("paradigm shift", ActionType.Replace, "fundamental change", "CORP_JARGON"),
        new("utilize", ActionType.Replace, "use", "CORP_JARGON"),
        new("moving the needle", ActionType.Replace, "achieving measurable results", "CORP_JARGON"),
    ];

    private static readonly IReadOnlyList<(Rule Rule, Regex Regex)> Compiled =
        Rules.Select(r => (r, new Regex(
            $@"\b{Regex.Escape(r.BannedPhrase)}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))).ToList();

    public sealed record Result(ContentDocument Document, int FlaggedCount);

    /// <summary>Clean a single string; returns (cleaned, flaggedCount).</summary>
    public static (string Text, int Flagged) Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, 0);
        var flagged = 0;
        var working = text;
        foreach (var (rule, regex) in Compiled)
        {
            if (!regex.IsMatch(working)) continue;
            flagged += regex.Matches(working).Count;
            working = rule.Action switch
            {
                ActionType.Strip => regex.Replace(working, string.Empty),
                ActionType.Replace => regex.Replace(working, rule.Replacement ?? string.Empty),
                // Pass 1 leaves RESTRUCTURE for the (future) LLM pass; count only.
                _ => working,
            };
        }
        // Normalize whitespace introduced by STRIP.
        working = Regex.Replace(working, @"[ \t]{2,}", " ");
        working = Regex.Replace(working, @"\s+([.,;:!?])", "$1");
        return (working.Trim().Length == 0 ? text : working, flagged);
    }

    private sealed class Counter { public int Value; }

    /// <summary>Apply the guardrail across every Run in a generated document.</summary>
    public static Result Apply(ContentDocument doc)
    {
        var counter = new Counter();
        var lede = CleanSection(doc.Lede, counter);
        var sections = doc.Sections.Select(s => CleanSection(s, counter)).ToList();
        return new Result(new ContentDocument(lede, sections), counter.Value);
    }

    private static Section CleanSection(Section s, Counter counter)
    {
        var heading = s.Heading;
        if (!string.IsNullOrEmpty(heading))
        {
            var (h, f) = Clean(heading);
            heading = h;
            counter.Value += f;
        }
        var paragraphs = s.Paragraphs.Select(p => CleanParagraph(p, counter)).ToList();
        var children = s.Children.Select(c => CleanSection(c, counter)).ToList();
        return s with { Heading = heading, Paragraphs = paragraphs, Children = children };
    }

    private static Paragraph CleanParagraph(Paragraph p, Counter counter)
    {
        switch (p)
        {
            case TextParagraph tp:
                return new TextParagraph(CleanRuns(tp.Runs, counter));
            case ListParagraph lp:
                var items = lp.Items.Select(item => CleanRuns(item, counter)).ToList();
                return new ListParagraph(lp.Ordered, items);
            default:
                return p;
        }
    }

    private static IReadOnlyList<Run> CleanRuns(IReadOnlyList<Run> runs, Counter counter)
    {
        var result = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            var (text, f) = Clean(run.Text);
            counter.Value += f;
            result.Add(run with { Text = text });
        }
        return result;
    }
}
