using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The publisher's own positions: the named sections of their site page, each with its text.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-06, reading a pillar: "It is again inventing its own Methodology versus using mine?"
/// His site states The Methodology, Seamless Integrations, Artificial Intelligence Use Cases, Clone
/// Yourself Work 24/7, Redefine Your Business Efficiency -- each a heading with its text, each in the
/// corpus. None of it reached the writer. The site was asked one keyword-shaped question and gave back
/// whatever ranked for the keyword; a section about how the publisher works does not.
/// </para>
/// <para>
/// So this does not retrieve. It reads the site page from the crawl run and cuts it at its second-level
/// headings: every section is a position, by heading, with its blocks' text in order. Which headings
/// exist is the site's business, not this code's -- nothing here names one -- and the writer is told
/// how each kind is used (<see cref="GccPublisherPositions"/>).
/// </para>
/// <para>
/// A page that is not in the run, or has no sections, is a warning the run records, not a silent
/// empty block: the operator should know the writer had none of their positions.
/// </para>
/// </remarks>
public sealed class GccPublisherPositionsReader(IGccCrawlPageReader pages, ILogger<GccPublisherPositionsReader> logger)
{
    public async Task<(IReadOnlyList<GccPublisherPosition> Positions, string? Warning)> ReadAsync(
        Guid siteRunId, string siteUrl, CancellationToken ct = default)
    {
        var candidates = await pages.ListPagesBySeedsAsync(siteRunId, [siteUrl], ct);
        var page = candidates.FirstOrDefault(p => SameAddress(p.FinalUrl, siteUrl) || SameAddress(p.Url, siteUrl))
            ?? candidates.FirstOrDefault();
        if (page is null)
        {
            logger.LogWarning("The publisher's site page {Url} is not in crawl run {RunId}; no positions.", siteUrl, siteRunId);
            return ([], $"The publisher's site page {siteUrl} is not in its crawl run, so the writer has none of the publisher's own positions.");
        }

        var positions = Cut(page.Blocks, page.FinalUrl is { Length: > 0 } f ? f : page.Url);
        if (positions.Count == 0)
        {
            logger.LogWarning("The publisher's site page {Url} has no sections with text; no positions.", siteUrl);
            return ([], $"The publisher's site page {siteUrl} has no sections with text, so the writer has none of the publisher's own positions.");
        }

        return (positions, null);
    }

    /// <summary>
    /// The page's sections at heading level two, each with the text of the blocks under it. A level-one
    /// heading is the page's title and starts nothing; a deeper heading is text within its section.
    /// A section with no text is not a position.
    /// </summary>
    internal static IReadOnlyList<GccPublisherPosition> Cut(JsonElement? blocks, string url)
    {
        if (blocks is not { ValueKind: JsonValueKind.Array } array) return [];

        var positions = new List<GccPublisherPosition>();
        string? heading = null;
        var text = new List<string>();

        void Flush()
        {
            if (heading is not null && text.Count > 0
                && !positions.Any(p => string.Equals(p.Heading, heading, StringComparison.OrdinalIgnoreCase)))
            {
                positions.Add(new GccPublisherPosition(heading, [.. text], url));
            }

            heading = null;
            text.Clear();
        }

        foreach (var block in array.EnumerateArray())
        {
            if (GccCorpusBlockMapper.ReadHeading(block) is { } h)
            {
                if (h.Level <= 1)
                {
                    Flush();
                    continue;
                }

                if (h.Level == 2)
                {
                    Flush();
                    heading = h.Text;
                    continue;
                }

                if (heading is not null) text.Add(h.Text);
                continue;
            }

            if (heading is null) continue;
            var line = TextOf(block);
            if (line.Length > 0) text.Add(line);
        }

        Flush();
        return positions;
    }

    private static string TextOf(JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object) return string.Empty;

        if (block.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
        {
            var parts = cells.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.String)
                .Select(c => c.GetString()!.Trim())
                .Where(c => c.Length > 0);
            return string.Join(" | ", parts);
        }

        return block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!.Trim()
            : string.Empty;
    }

    private static bool SameAddress(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        static string Normalize(string url)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return url.Trim().TrimEnd('/');
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return $"{host}{uri.AbsolutePath}".TrimEnd('/');
        }
    }
}
