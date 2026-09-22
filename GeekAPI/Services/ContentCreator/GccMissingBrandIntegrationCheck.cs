namespace GeekAPI.Services.ContentCreator;

/// <summary>One partner's presence, or absence, in the keyword's own SERP.</summary>
/// <remarks>
/// <c>MentionedInTitle</c> is a weak signal on purpose: it checks organic <i>titles</i> only,
/// because that is all a curated brief carries (<c>ContentBrief.serpTitles</c>) — Google result
/// snippets are not persisted anywhere in this pipeline. A true snippet check would need the
/// snippet text itself, which does not exist here; stating that plainly is better than a check
/// that silently checks less than its name implies.
/// </remarks>
public sealed record GccPartnerSerpPresence(
    string PartnerUrl,
    string PartnerDomain,
    bool DomainInTopNOrganics,
    bool MentionedInTitle,
    int? OrganicRank);

/// <summary>
/// Stage 8b, rescoped 2026-09-22: whether a partner's own SERP presence is missing for this
/// keyword. Not "does a competitor's best-X-tools listicle mention this partner" — that requires
/// the listicle's own body text, which is in no crawl unless it is itself a declared competitor,
/// and fetching arbitrary ranking URLs was explicitly rejected (new crawl path, new persisted
/// rows). This is the honest, computable substitute: is the partner's domain even ranking for this
/// keyword at all, in the SERP the operator already curated.
/// </summary>
public static class GccMissingBrandIntegrationCheck
{
    /// <summary>
    /// <paramref name="organicTitles"/> and <paramref name="organicUrls"/> are paired by index —
    /// the same convention <c>ContentBrief.serpTitles</c>/<c>serpUrls</c> already uses (built from
    /// the same selected-organics array, same order, in <c>buildCuratedSerpSeed</c>). Ragged input
    /// (mismatched lengths) is truncated to the shorter list rather than throwing — a partial SERP
    /// upload is still real evidence, the same tolerance the resolver family already applies.
    /// </summary>
    public static IReadOnlyList<GccPartnerSerpPresence> Evaluate(
        IReadOnlyList<string> partnerUrls,
        IReadOnlyList<string> organicTitles,
        IReadOnlyList<string> organicUrls,
        int topN = 10)
    {
        var pairCount = Math.Min(organicTitles.Count, organicUrls.Count);
        var topOrganics = Enumerable.Range(0, Math.Min(pairCount, Math.Max(topN, 0)))
            .Select(i => (Title: organicTitles[i], Url: organicUrls[i]))
            .ToList();

        var results = new List<GccPartnerSerpPresence>();
        foreach (var partnerUrl in partnerUrls)
        {
            var domain = TryDomain(partnerUrl);
            if (domain is null)
            {
                continue;
            }

            var rankedAt = topOrganics.FindIndex(o =>
                string.Equals(TryDomain(o.Url), domain, StringComparison.OrdinalIgnoreCase));
            var mentioned = topOrganics.Any(o =>
                o.Title.Contains(domain, StringComparison.OrdinalIgnoreCase)
                || (DomainLabel(domain) is { Length: > 2 } label
                    && o.Title.Contains(label, StringComparison.OrdinalIgnoreCase)));

            results.Add(new GccPartnerSerpPresence(
                partnerUrl,
                domain,
                DomainInTopNOrganics: rankedAt >= 0,
                MentionedInTitle: mentioned,
                OrganicRank: rankedAt >= 0 ? rankedAt + 1 : null));
        }

        return results;
    }

    private static string? TryDomain(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>The registrable label before the first dot — "acme" from "acme.com" — for a
    /// looser title match than the full domain string, which titles rarely spell out verbatim.</summary>
    private static string? DomainLabel(string domain)
    {
        var dot = domain.IndexOf('.');
        return dot > 0 ? domain[..dot] : null;
    }
}
