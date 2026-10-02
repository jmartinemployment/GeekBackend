using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// One partner's share of a create's grounding: the product, and only that product's evidence.
/// </summary>
/// <param name="Host">
/// The bucket key, from <see cref="GccRequiredToolMentions.HostKeyOf"/>. The same key the partner name
/// lookup is built on, deliberately — a page bucketed by any other rule belongs to no partner.
/// </param>
/// <param name="ProductName">
/// What the page is about. The subject, never the create's Topic: Topic is the problem
/// (<see cref="GccTopic"/>), and one value cannot be both.
/// </param>
/// <param name="Pages">That partner's retrieved pages, sliced out of the create's pooled research.</param>
/// <param name="Passages">The same pages as typed blocks, for the block quotation.</param>
public sealed record GccPartnerToolSlice(
    string Host,
    string ProductName,
    IReadOnlyList<GccQuoteablePage> Pages,
    IReadOnlyList<GccGroundedPassage> Passages)
{
    /// <summary>The create as this partner alone would see it — pooled research narrowed to these pages.</summary>
    public GccCreateDto Narrow(GccCreateDto create)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        var narrowed = research is null
            ? new GccResearchDocument(null, Pages)
            : research with { Quoteables = [.. Pages] };
        return create with { ResearchJson = GccResearchFetchService.Serialize(narrowed) };
    }
}

/// <summary>
/// Splits a create's pooled partner evidence into one slice per declared partner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why slicing is the fix.</b> A tool page is about one product. The Create path passed
/// <c>create.Topic</c> as the product name and handed extraction the whole pool, so it searched every
/// partner's pages for a product named after the keyword. Two live runs, two different partner sets:
/// 1 of 22 payload categories populated, against partners carrying 84–226 quotable spans each.
/// <c>GccV2PartnerExtractionService</c>'s <c>toolNames</c> is prompt-only — it filters nothing — so the
/// page slice is what actually scopes extraction.
/// </para>
/// <para>
/// <b>Shared on purpose.</b> Jeff, 2026-10-02: five tool pages at once is the priority, and single tool
/// pages are also needed. Both want the same unit — the set-of-five loops every slice, a single page
/// takes one — so the slicing and naming live here rather than in either caller.
/// </para>
/// <para>
/// <b>A partner with no retrieved pages still gets a slice.</b> It will refuse its own page, by name,
/// at the sufficiency gate. Dropping it here would make a declared partner disappear with no error,
/// which is the silent degrade this codebase refuses everywhere.
/// </para>
/// </remarks>
public static class GccPartnerToolSlices
{
    /// <summary>
    /// One slice per declared partner URL, in the operator's declared order.
    /// </summary>
    /// <remarks>
    /// Keyed off the declared URLs rather than off whatever the pool happens to contain, so the result
    /// is the partners the project committed to — not a set that shrinks when retrieval has a bad day.
    /// </remarks>
    public static IReadOnlyList<GccPartnerToolSlice> Build(
        GccCreateDto create,
        IReadOnlyList<string> partnerUrls,
        IReadOnlyList<GccGroundedPassage> partnerPassages)
    {
        if (partnerUrls.Count == 0) return [];

        var names = GccRequiredToolMentions.AnchorLookup(create.BriefJson, partnerUrls);
        var pooled = GccResearchFetchService.Deserialize(create.ResearchJson)?.Quoteables ?? [];

        var pagesByHost = Bucket(pooled, page => page.Url);
        var passagesByHost = Bucket(partnerPassages, passage => passage.Url);

        var slices = new List<GccPartnerToolSlice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in partnerUrls)
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length == 0 || !seen.Add(host)) continue;

            // No name means no page: the product is the subject, and a page about an unnamed product
            // would fall back to the keyword, which is the defect this exists to remove. AnchorLookup
            // host-derives a name for any valid URL, so this is a malformed declaration rather than a
            // missing brief row.
            if (!names.TryGetValue(host, out var productName) || string.IsNullOrWhiteSpace(productName))
            {
                continue;
            }

            slices.Add(new GccPartnerToolSlice(
                host,
                productName.Trim(),
                pagesByHost.TryGetValue(host, out var pages) ? pages : [],
                passagesByHost.TryGetValue(host, out var passages) ? passages : []));
        }

        return slices;
    }

    /// <summary>
    /// The one slice for a named product, or null when that name matches no declared partner.
    /// </summary>
    /// <remarks>
    /// The single-page entry point. Matching is on the name the lookup holds, so an operator asking for
    /// "Dext" gets dext.com's evidence and nothing else. Null rather than a guess: generating a page for
    /// a product this project declared no partner for would have no evidence to be grounded in.
    /// </remarks>
    public static GccPartnerToolSlice? ForProduct(
        GccCreateDto create,
        IReadOnlyList<string> partnerUrls,
        IReadOnlyList<GccGroundedPassage> partnerPassages,
        string productName)
    {
        var wanted = (productName ?? string.Empty).Trim();
        if (wanted.Length == 0) return null;

        return Build(create, partnerUrls, partnerPassages)
            .FirstOrDefault(slice => string.Equals(slice.ProductName, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, List<T>> Bucket<T>(
        IReadOnlyList<T> items, Func<T, string?> urlOf)
    {
        var buckets = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var host = GccRequiredToolMentions.HostKeyOf(urlOf(item));
            if (host.Length == 0) continue;
            if (!buckets.TryGetValue(host, out var bucket))
            {
                bucket = [];
                buckets[host] = bucket;
            }

            bucket.Add(item);
        }

        return buckets;
    }
}
