using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>One figure found in prose: what was written, where, and its value for comparison.</summary>
public sealed record GccFigure(string Written, int Index, string Value);

/// <summary>
/// What counts as a figure in a draft -- the claims "a number may appear only if it is in the
/// evidence" is about -- and what does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>A figure is</b>, in paragraph text:
/// </para>
/// <list type="bullet">
/// <item>a percentage: <c>73%</c>, <c>4.5 percent</c>;</item>
/// <item>a currency amount: <c>$19</c>, <c>€1,200</c>, <c>£3.50</c>;</item>
/// <item>a multiplier: <c>3x</c>, <c>2.5×</c>;</item>
/// <item>a count with a unit of time or of the work being counted: <c>40 hours</c>, <c>9 days</c>,
/// <c>200 invoices</c>, <c>12 seats</c> (the units are <see cref="Units"/>);</item>
/// <item>any number of three or more digits: <c>3,000</c>, <c>250</c>, a year in prose.</item>
/// </list>
/// <para>
/// <b>Not a figure</b>: anything in a heading ("7 signs your process is broken" is a list title, not a
/// claim); an ordinal (<c>3rd</c>, <c>100th</c>); a list numeral (<c>1.</c>, <c>2)</c> opening an
/// item, <c>Step 3</c>); a year inside a cite -- a run carrying a link, or parentheses such as
/// "(Melio, 2025)"; a version number (<c>v2.1</c>, <c>version 3.4</c>, <c>1.2.3</c>); and a bare
/// small number with no unit ("3 ways", "two partners"), which carries no claim a reader could check.
/// </para>
/// <para>
/// Why it is written down rather than "any digits": the first version checked every digit run, so a
/// list heading needed its number licensed by the evidence, and the test fixtures were changed to
/// lettered headings to get past it -- the fixture dodging the check. Widening the evidence to the
/// prompt's own instruction numbers (word floors, "600-850 words") instead would have licensed
/// "3,000" because the prompt said it, which licenses nothing. A grammar keeps the check on claims.
/// </para>
/// <para>
/// Comparison is by value: thousands separators dropped and a trailing decimal zero ignored, so
/// "3,000" matches "3000" and "$19.00" matches "19". A figure is licensed when its value appears as a
/// number anywhere in the evidence.
/// </para>
/// </remarks>
public static partial class GccFigureGrammar
{
    /// <summary>Units that make a count a claim: time, and the things an AP or ops team counts.</summary>
    public static readonly IReadOnlyList<string> Units =
    [
        "second", "seconds", "minute", "minutes", "hour", "hours", "day", "days", "week", "weeks",
        "month", "months", "year", "years",
        "invoice", "invoices", "bill", "bills", "payment", "payments", "transaction", "transactions",
        "document", "documents", "receipt", "receipts", "vendor", "vendors", "supplier", "suppliers",
        "user", "users", "seat", "seats", "employee", "employees", "customer", "customers",
        "client", "clients", "error", "errors", "integration", "integrations", "approval", "approvals",
        "entity", "entities", "currency", "currencies", "country", "countries", "language", "languages",
    ];

    /// <summary>The figures in one paragraph.</summary>
    /// <param name="text">The paragraph's text, runs concatenated.</param>
    /// <param name="citeRanges">Character ranges of runs that carry a link -- a cite, where a year is
    /// attribution rather than a claim.</param>
    public static IReadOnlyList<GccFigure> Find(string text, IReadOnlyList<(int Start, int End)>? citeRanges = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var found = new List<GccFigure>();
        var taken = new List<(int Start, int End)>();

        void Add(Match m, Group value)
        {
            if (taken.Any(t => m.Index < t.End && t.Start < m.Index + m.Length)) return;
            if (IsListNumeral(text, m.Index) || IsVersion(text, m.Index, m.Length)) return;
            taken.Add((m.Index, m.Index + m.Length));
            found.Add(new GccFigure(m.Value.Trim(), m.Index, Normalize(value.Value)));
        }

        foreach (Match m in Percentage().Matches(text)) Add(m, m.Groups["n"]);
        foreach (Match m in Currency().Matches(text)) Add(m, m.Groups["n"]);
        foreach (Match m in Multiplier().Matches(text)) Add(m, m.Groups["n"]);
        foreach (Match m in CountWithUnit().Matches(text))
        {
            if (Units.Contains(m.Groups["unit"].Value, StringComparer.OrdinalIgnoreCase)) Add(m, m.Groups["n"]);
        }

        foreach (Match m in ThreeOrMoreDigits().Matches(text))
        {
            if (IsCiteYear(text, m, citeRanges)) continue;
            Add(m, m.Groups["n"]);
        }

        return [.. found.OrderBy(f => f.Index)];
    }

    /// <summary>Every number in <paramref name="evidence"/>, normalized for comparison.</summary>
    public static IReadOnlySet<string> NumbersIn(string? evidence) =>
        AnyNumber().Matches(evidence ?? string.Empty).Select(m => Normalize(m.Value)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Thousands separators dropped; a trailing decimal zero is not a different figure.</summary>
    public static string Normalize(string number)
    {
        var digits = number.Replace(",", string.Empty);
        if (digits.Contains('.')) digits = digits.TrimEnd('0').TrimEnd('.');
        return digits;
    }

    /// <summary>"1." or "2)" opening an item, or "Step 3".</summary>
    private static bool IsListNumeral(string text, int index)
    {
        var before = text[..index];
        if (before.Trim().Length == 0)
        {
            var after = text[index..];
            if (LeadingListMarker().IsMatch(after)) return true;
        }

        return before.TrimEnd().EndsWith("Step", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"v2.1", "version 3.4", "1.2.3".</summary>
    private static bool IsVersion(string text, int index, int length)
    {
        var before = text[..index].TrimEnd();
        if (before.EndsWith("version", StringComparison.OrdinalIgnoreCase)) return true;
        if (index > 0 && (text[index - 1] is 'v' or 'V')) return true;
        var rest = text[(index + length)..];
        return rest.StartsWith('.') && rest.Length > 1 && char.IsDigit(rest[1]);
    }

    /// <summary>A year (1900-2099) inside a link run or inside parentheses.</summary>
    private static bool IsCiteYear(string text, Match m, IReadOnlyList<(int Start, int End)>? citeRanges)
    {
        if (m.Groups["n"].Value.Length != 4
            || !int.TryParse(m.Groups["n"].Value, out var year)
            || year is < 1900 or > 2099)
        {
            return false;
        }

        if (citeRanges?.Any(r => m.Index >= r.Start && m.Index < r.End) == true) return true;

        var open = text.LastIndexOf('(', m.Index);
        var close = text.LastIndexOf(')', m.Index);
        return open >= 0 && open > close && text.IndexOf(')', m.Index) > m.Index;
    }

    [GeneratedRegex(@"(?<![\w.])(?<n>\d+(?:\.\d+)?)\s?(?:%|percent\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Percentage();

    [GeneratedRegex(@"[$€£]\s?(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)")]
    private static partial Regex Currency();

    [GeneratedRegex(@"(?<![\w.])(?<n>\d+(?:\.\d+)?)\s?[x×](?!\w)")]
    private static partial Regex Multiplier();

    [GeneratedRegex(@"(?<![\w.,$€£])(?<n>\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?\s+(?<unit>[A-Za-z]+)\b")]
    private static partial Regex CountWithUnit();

    [GeneratedRegex(@"(?<![\w.,$€£])(?<n>\d{1,3}(?:,\d{3})+|\d{3,})(?:\.\d+)?(?![\w%×]|\.\d|,\d)")]
    private static partial Regex ThreeOrMoreDigits();

    [GeneratedRegex(@"^\s*\d+[.)]\s")]
    private static partial Regex LeadingListMarker();

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?")]
    private static partial Regex AnyNumber();
}
