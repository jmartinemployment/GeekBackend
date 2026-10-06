using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// A competitor's identity, kept out of what the writer reads.
/// </summary>
/// <remarks>
/// <para>
/// Competitors are read and never cited, named or linked -- the competitor blocks have said so since
/// they existed. They also printed each competitor's URL, page titles, headings and passages, so the
/// name sat in front of the writer dozens of times beside a rule not to use it. On 2026-10-06 the
/// 8:26 run's pillar named Tipalti, was told so by name on its retry, named it again, and the run was
/// refused with nothing saved. An instruction against a name the prompt keeps repeating is a weak
/// instruction; the strong one is not to show the name.
/// </para>
/// <para>
/// The name is derived from each competitor page's host -- <c>tipalti.com</c> gives <c>Tipalti</c> --
/// and both the host and the name are replaced wherever they occur in the block's text, as whole words,
/// whatever their case. The URL line itself becomes a numbered label. Heading text is redacted the same
/// way in the provenance lookup set, so a <c>competitor:&lt;heading&gt;</c> tag written from the shown
/// text still matches.
/// </para>
/// <para>
/// This is sanitising the writer's input, not producing anything: nothing is added, and the words that
/// go are the ones the writer is forbidden to use. The reader never sees this text.
/// </para>
/// </remarks>
public static class GccCompetitorNames
{
    public const string Redacted = "a competitor";

    /// <summary>
    /// The hosts and host-derived names of these pages, longest first so <c>tipalti.com</c> goes before
    /// <c>Tipalti</c> would otherwise leave <c>.com</c> behind. Empty for no URLs or none that parse.
    /// </summary>
    public static IReadOnlyList<string> FromUrls(IEnumerable<string?> urls)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) continue;
            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
            if (host.Length == 0) continue;

            names.Add(host);
            var label = host.Split('.').FirstOrDefault() ?? string.Empty;
            // Two letters is a word, not a name: redacting "ap" or "us" would take ordinary prose with it.
            if (label.Length >= 3) names.Add(label);
        }

        return [.. names.OrderByDescending(n => n.Length)];
    }

    /// <summary>
    /// Tool names as given -- the site's tools that are not this project's partners -- trimmed,
    /// distinct, longest first, for the same redaction. Two-letter names are kept out for the reason
    /// <see cref="FromUrls"/> gives.
    /// </summary>
    public static IReadOnlyList<string> Names(IEnumerable<string?> toolNames) =>
        [.. toolNames
            .Select(n => n?.Trim() ?? string.Empty)
            .Where(n => n.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)];

    /// <summary>
    /// Whether <paramref name="text"/> names any of <paramref name="names"/> as a whole word, whatever
    /// the case. A URL counts: <c>/tools/accounting/accounts-payable/tipalti</c> names Tipalti.
    /// </summary>
    public static bool Mentions(string? text, IReadOnlyList<string> names) =>
        !string.IsNullOrEmpty(text) && names.Any(name => Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){Regex.Escape(name)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase));

    /// <summary>
    /// <paramref name="text"/> with every whole-word occurrence of each name replaced. The text itself
    /// when there are no names.
    /// </summary>
    public static string Redact(string text, IReadOnlyList<string> names)
    {
        if (names.Count == 0 || string.IsNullOrEmpty(text)) return text;

        var result = text;
        foreach (var name in names)
        {
            result = Regex.Replace(
                result,
                $@"(?<![A-Za-z0-9]){Regex.Escape(name)}(?![A-Za-z0-9])",
                Redacted,
                RegexOptions.IgnoreCase);
        }

        return result;
    }
}
