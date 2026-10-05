using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>One amount of money found in text.</summary>
/// <param name="Currency">The ISO code the text gives it ("USD", "AUD", "GBP"...), or empty for a bare
/// <c>$</c> with no currency named.</param>
public sealed record GccMoney(string Written, int Index, string Value, string Currency)
{
    public bool IsBareDollar => Currency.Length == 0;
    public bool IsUsd => Currency == GccCurrencyGrammar.Usd;
    public bool IsForeign => !IsBareDollar && !IsUsd;
}

/// <summary>
/// Which currency an amount of money is written in. Money in a draft is US dollars or it is not there
/// (Jeff, 2026-10-05: "Any time a currency is listed needs to be in USD"), after a tool page stated
/// ApprovalMax's prices in Australian dollars because that is how the crawled page gave them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recognised:</b> a symbol or code before the amount (<c>A$39</c>, <c>AU$39</c>, <c>AUD 39</c>,
/// <c>£12</c>, <c>€1,200</c>, <c>US$15</c>, <c>USD 15</c>); a code or name after it (<c>$39 AUD</c>,
/// <c>39 AUD</c>, <c>39 euros</c>, <c>$15 USD</c>, <c>39 Australian dollars</c>); and a bare <c>$39</c>,
/// which names no currency.
/// </para>
/// <para>
/// <b>A bare $ takes the currency its line names.</b> "From $39 per user (billed in AUD)" is an
/// Australian-dollar price though no code touches the number. Only within one line: the evidence is
/// many pages joined, and a code on one page says nothing about a price on another.
/// </para>
/// <para>
/// Never converted. A price the evidence gives only in another currency is not a price this page can
/// state: a conversion is a figure nobody published.
/// </para>
/// </remarks>
public static partial class GccCurrencyGrammar
{
    public const string Usd = "USD";

    /// <summary>Every amount of money in <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<GccMoney> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var found = new List<GccMoney>();
        var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            FindInLine(line, offset, found);
            offset += line.Length + 1;
        }

        return [.. found.OrderBy(m => m.Index)];
    }

    private static void FindInLine(string line, int offset, List<GccMoney> into)
    {
        var taken = new List<(int Start, int End)>();

        void Add(Match m, string currency)
        {
            if (taken.Any(t => m.Index < t.End && t.Start < m.Index + m.Length)) return;
            taken.Add((m.Index, m.Index + m.Length));
            into.Add(new GccMoney(
                m.Value.Trim(), offset + m.Index, GccFigureGrammar.Normalize(m.Groups["n"].Value), currency));
        }

        foreach (Match m in UsdBefore().Matches(line)) Add(m, Usd);
        foreach (Match m in ForeignBefore().Matches(line)) Add(m, CodeOf(m.Groups["cur"].Value));
        foreach (Match m in DollarThenCode().Matches(line)) Add(m, CodeOf(m.Groups["cur"].Value));
        foreach (Match m in AmountThenCode().Matches(line)) Add(m, CodeOf(m.Groups["cur"].Value));

        // A bare $ names no currency itself. When its line names exactly one that is not US dollars,
        // that is the currency the line is quoting prices in.
        var lineCurrencies = Mentioned().Matches(line)
            .Select(m => CodeOf(m.Value))
            .Where(c => c != Usd)
            .Distinct()
            .ToList();
        var inherited = lineCurrencies.Count == 1 ? lineCurrencies[0] : string.Empty;
        foreach (Match m in BareDollar().Matches(line)) Add(m, inherited);
    }

    /// <summary>The ISO code for a symbol, code or name as written.</summary>
    internal static string CodeOf(string written)
    {
        var w = written.Trim().TrimEnd('.').ToUpperInvariant();
        return w switch
        {
            "US$" or "USD" or "US DOLLAR" or "US DOLLARS" or "U.S. DOLLAR" or "U.S. DOLLARS" => Usd,
            "A$" or "AU$" or "AUD" or "AUSTRALIAN DOLLAR" or "AUSTRALIAN DOLLARS" => "AUD",
            "C$" or "CA$" or "CAD" or "CANADIAN DOLLAR" or "CANADIAN DOLLARS" => "CAD",
            "NZ$" or "NZD" or "NEW ZEALAND DOLLAR" or "NEW ZEALAND DOLLARS" => "NZD",
            "S$" or "SGD" or "SINGAPORE DOLLAR" or "SINGAPORE DOLLARS" => "SGD",
            "HK$" or "HKD" => "HKD",
            "£" or "GBP" or "POUNDS STERLING" or "POUND STERLING" => "GBP",
            "€" or "EUR" or "EURO" or "EUROS" => "EUR",
            "¥" or "JPY" => "JPY",
            "₹" or "INR" => "INR",
            "R$" or "BRL" => "BRL",
            "MX$" or "MXN" => "MXN",
            _ => w,
        };
    }

    private const string Num = @"(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)";
    private const string Codes = "AUD|CAD|NZD|SGD|HKD|GBP|EUR|JPY|INR|BRL|MXN|CHF|ZAR|SEK|NOK|DKK|CNY";
    private const string Names =
        @"Australian dollars?|Canadian dollars?|New Zealand dollars?|Singapore dollars?|pounds? sterling|euros?";
    private const string UsdNames = @"US dollars?|U\.S\. dollars?";

    [GeneratedRegex(@"(?<![A-Za-z])(?:US\$|USD\s?\$?)\s?" + Num)]
    private static partial Regex UsdBefore();

    [GeneratedRegex(@"(?<![A-Za-z])(?<cur>AU\$|A\$|CA\$|C\$|NZ\$|HK\$|MX\$|S\$|R\$|£|€|¥|₹|(?:" + Codes + @")\s?\$?)\s?" + Num)]
    private static partial Regex ForeignBefore();

    [GeneratedRegex(@"\$\s?" + Num + @"\s?\(?(?<cur>USD|" + Codes + @")\b\)?")]
    private static partial Regex DollarThenCode();

    // Codes are matched as written, in capitals; only the spelled-out names ignore case.
    [GeneratedRegex(
        @"(?<![\w.,$])" + Num + @"\s?(?<cur>(?:USD|" + Codes + @")\b|(?i:" + UsdNames + "|" + Names + @")\b)")]
    private static partial Regex AmountThenCode();

    [GeneratedRegex(@"\$\s?" + Num)]
    private static partial Regex BareDollar();

    [GeneratedRegex(
        @"(?<![A-Za-z])(?:AU\$|A\$|CA\$|C\$|NZ\$|HK\$|MX\$|S\$|R\$)|£|€|¥|₹|\b(?:USD|" + Codes + @")\b|\b(?i:" + UsdNames + "|" + Names + @")\b")]
    private static partial Regex Mentioned();
}
