using System.Text;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// The one definition of what is done to a model's JSON reply before it is parsed: an ordered list of named repairs,
/// each of which changes syntax and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A repair here removes or escapes a character that carries no content, or cuts away text that is not the object. It
/// never adds a word, never fills a missing field, never guesses. The repaired text goes through the same parse and the
/// same checks as the reply as written, and a reply that is still invalid is refused by the parser, not repaired
/// further. A repair is added only when a real reply needed it.
/// </para>
/// <para>
/// Each candidate carries the names of the repairs that produced it, so a parse that succeeds on a repaired candidate
/// is visible: <see cref="JsonRepairTrace"/> records it and the run log shows a <c>repaired</c> event naming the
/// repairs. Before this existed the repairs were applied silently, so a reply that parsed only after a repair read in
/// the log exactly like one that was well formed.
/// </para>
/// <para>
/// This replaces the parser's private candidate list and the four code-fence strippers that had been copied into
/// <c>GccGenerateService</c>.
/// </para>
/// </remarks>
internal static class JsonReplySanitizer
{
    internal const string CodeFenceRepair = "code-fence";
    internal const string ExtractObjectRepair = "extract-object";
    internal const string LiteralNewlinesRepair = "literal-newlines";
    internal const string StrayQuoteRepair = "stray-quote-before-object";

    private static readonly Regex CodeFence = new(@"^```(?:json|html)?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex StrayQuoteBeforeObject = new(@",(\s*)""\{""", RegexOptions.Compiled);

    /// <summary>A text to try parsing, and the named repairs that produced it from the reply (none: as written).</summary>
    internal readonly record struct Candidate(string Text, IReadOnlyList<string> Repairs);

    /// <summary>
    /// The texts to try, in order: the reply as written (whitespace trimmed), then each repair and its combinations.
    /// A text that an earlier candidate already offered is not offered again.
    /// </summary>
    internal static IEnumerable<Candidate> Candidates(string raw)
    {
        var asWritten = new Candidate(raw.Trim(), []);
        var seen = new HashSet<string>(StringComparer.Ordinal) { asWritten.Text };
        yield return asWritten;

        var unfenced = Apply(asWritten, CodeFenceRepair, StripCodeFence);
        if (unfenced is { } fenceless && seen.Add(fenceless.Text))
        {
            yield return fenceless;
        }

        var basis = unfenced ?? asWritten;
        foreach (var candidate in Downstream(basis, seen))
        {
            yield return candidate;
        }

        var unquoted = Apply(basis, StrayQuoteRepair, RepairStrayQuoteBeforeObject);
        if (unquoted is { } quoteless)
        {
            if (seen.Add(quoteless.Text))
            {
                yield return quoteless;
            }

            foreach (var candidate in Downstream(quoteless, seen))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>The names of the repairs that changed this reply's text, in the order they were tried.</summary>
    internal static string RepairsTried(string raw)
    {
        var names = Candidates(raw).SelectMany(c => c.Repairs).Distinct().ToList();
        return names.Count == 0 ? "none (no named repair changed the reply)" : string.Join(", ", names);
    }

    /// <summary>Removes a Markdown-style code fence the model wrapped around its JSON.</summary>
    internal static string StripCodeFence(string text) => CodeFence.Replace(text, string.Empty).Trim();

    /// <summary>
    /// Removes a quote the model left in front of an object that follows a comma, as in
    /// <c>{"text":"..."},"{"text":"Stampli","href":"..."}</c> (the Stampli lede of 2026-10-07, whose one stray
    /// character cost a whole tool page).
    /// </summary>
    internal static string RepairStrayQuoteBeforeObject(string json) =>
        StrayQuoteBeforeObject.Replace(json, ",$1{\"");

    /// <summary>
    /// Escapes a raw line break inside a JSON string, which the parser rejects; the break stays in the text as the
    /// escape that means it.
    /// </summary>
    internal static string RepairLiteralNewlinesInJsonStrings(string json)
    {
        var result = new StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        foreach (var ch in json)
        {
            if (escaped)
            {
                result.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                result.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                result.Append(ch);
                continue;
            }

            if (inString && (ch == '\r' || ch == '\n'))
            {
                result.Append("\\n");
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    /// <summary>
    /// Finds the first "{" or "[" and scans for its actual matching close (tracking nesting depth
    /// across both bracket types and skipping over brackets inside string literals) rather than
    /// naively grabbing through the last "}" anywhere in the response. A model that appends any
    /// trailing text after valid JSON -- a closing remark, an aside that happens to contain a brace
    /// or bracket character -- would otherwise get that trailing content spliced into the "JSON"
    /// handed to the deserializer, breaking parsing even though the actual JSON was well-formed.
    /// Returns null if no balanced close is found (e.g. the response was genuinely truncated
    /// mid-object/mid-array).
    /// </summary>
    internal static string? ExtractJsonObject(string raw)
    {
        var start = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] is '{' or '[')
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < raw.Length; i++)
        {
            var ch = raw[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch is '{' or '[')
            {
                depth++;
            }
            else if (ch is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return raw[start..(i + 1)];
                }
            }
        }

        return null;
    }

    /// <summary>The object cut out of the text, then its raw line breaks escaped.</summary>
    private static IEnumerable<Candidate> Downstream(Candidate start, HashSet<string> seen)
    {
        var extracted = Apply(start, ExtractObjectRepair, ExtractJsonObject);
        if (extracted is { } cutOut && seen.Add(cutOut.Text))
        {
            yield return cutOut;
        }

        var spaced = Apply(extracted ?? start, LiteralNewlinesRepair, RepairLiteralNewlinesInJsonStrings);
        if (spaced is { } escapedBreaks && seen.Add(escapedBreaks.Text))
        {
            yield return escapedBreaks;
        }
    }

    /// <summary>The repaired candidate, or null when the repair changed nothing or produced nothing.</summary>
    private static Candidate? Apply(Candidate from, string name, Func<string, string?> repair)
    {
        var repaired = repair(from.Text);
        if (string.IsNullOrWhiteSpace(repaired) || string.Equals(repaired, from.Text, StringComparison.Ordinal))
        {
            return null;
        }

        return new Candidate(repaired, [.. from.Repairs, name]);
    }
}
