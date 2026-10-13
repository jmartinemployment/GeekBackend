using System.Globalization;
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
/// <para>
/// <b>A number the evidence writes with a scale is also the number it stands for</b> (2026-10-12):
/// "200K+" is 200,000, "$300M" is 300,000,000, "1.5 million" is 1,500,000. A vendor's page states its
/// own totals this way ("100K+ filings on-time, 200K+ hours saved"), and a writer who writes one out
/// in full is stating the figure it was shown. Read as digits alone "200K+" licensed 200 and nothing
/// else, so the 15:29 UTC run of 2026-10-12 refused a whole blog for "200,000 hours saved", taken
/// from exactly that line of a partner's page. The digits as written stay licensed too, so
/// "$300 million" still matches "$300M". This reads the evidence; what counts as a figure in a draft
/// is unchanged, and a draft's own "200K+" is still not checked.
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
            if (IsListNumeral(text, m.Index) || IsVersion(text, m.Index, m.Length) || IsNamedList(text, m.Index)) return;
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

    /// <summary>
    /// Every number in <paramref name="evidence"/>, normalized for comparison: each as it is written,
    /// and each one written with a scale ("200K+", "$300M", "1.5 million") as the number it stands for.
    /// </summary>
    public static IReadOnlySet<string> NumbersIn(string? evidence)
    {
        var text = evidence ?? string.Empty;
        var known = AnyNumber().Matches(text).Select(m => Normalize(m.Value)).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in ScaledBySuffix().Matches(text)) AddScaled(known, m);
        foreach (Match m in MoneyScaledBySuffix().Matches(text)) AddScaled(known, m);
        foreach (Match m in ScaledByWord().Matches(text)) AddScaled(known, m);
        return known;
    }

    /// <summary>
    /// The number a scaled figure stands for, added to <paramref name="known"/>. A figure too large to
    /// hold, or one that is not a number after all, adds nothing: its digits are already licensed as
    /// written.
    /// </summary>
    private static void AddScaled(HashSet<string> known, Match m)
    {
        var factor = FactorOf(m.Groups["scale"].Value);
        if (factor == 0m) return;
        if (!decimal.TryParse(
                m.Groups["n"].Value.Replace(",", string.Empty),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return;
        }

        if (number > decimal.MaxValue / factor) return;
        known.Add(Normalize((number * factor).ToString("0.############", CultureInfo.InvariantCulture)));
    }

    /// <summary>What a scale multiplies by: K a thousand, M a million, B or bn a billion, and the words.</summary>
    private static decimal FactorOf(string scale) => scale.ToLowerInvariant() switch
    {
        "k" or "thousand" => 1_000m,
        "m" or "million" => 1_000_000m,
        "b" or "bn" or "billion" => 1_000_000_000m,
        "trillion" => 1_000_000_000_000m,
        _ => 0m,
    };

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

    /// <summary>
    /// The lists of companies and the market indexes whose name ends in a number: "Fortune 500",
    /// "S&amp;P 500", "Russell 2000", "FTSE 100", "Nasdaq 100".
    /// </summary>
    private static readonly string[] NamedLists = ["Fortune", "S&P", "Russell", "FTSE", "Nasdaq"];

    /// <summary>
    /// Whether the number at <paramref name="index"/> is the end of a list's name and not a figure.
    /// </summary>
    /// <remarks>
    /// "Fortune 500" names a list of companies; it states no count of anything. The 17:47 UTC run of
    /// 2026-10-13 refused TaxJar's tool page for "it is not built for the Fortune 500", the 500 being
    /// in none of the evidence. The name is written with a capital and one space or a hyphen before
    /// its number; "a fortune 500 times over" is still a figure.
    /// </remarks>
    private static bool IsNamedList(string text, int index)
    {
        if (index < 2 || text[index - 1] is not (' ' or '-' or '\u00A0')) return false;
        var before = text[..(index - 1)];
        return NamedLists.Any(name =>
            before.EndsWith(name, StringComparison.Ordinal)
            && (before.Length == name.Length || !char.IsLetterOrDigit(before[before.Length - name.Length - 1])));
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

    // "200K+", "200k", "$300M", "4B": the letter straight after the number, and nothing after it that
    // would make the pair part of a longer word or an identifier ("3K9F"). A small m or b is a scale
    // only on money, below: "5m" is five metres or minutes and "802.11b" is a standard.
    [GeneratedRegex(@"(?<![\w.])(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<scale>[kK]|M|B)(?![A-Za-z0-9])")]
    private static partial Regex ScaledBySuffix();

    // "$10m", "$2bn", "€1.5M": on an amount of money the letter is a scale in either case.
    [GeneratedRegex(@"[$€£]\s?(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<scale>bn|[kmb])(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyScaledBySuffix();

    // "3 million", "1.2 billion", "200 thousand", "5-million".
    [GeneratedRegex(@"(?<![\w.])(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)[\s-]?(?<scale>thousand|million|billion|trillion)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScaledByWord();
}
