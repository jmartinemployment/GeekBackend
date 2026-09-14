using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>Markdown verify for competitor claim-bearing assets (competitor-extraction §4).</summary>
public static class GccV2CompetitorExtractionVerify
{
    public static async Task<GccCompetitorExtractionDocument> VerifyAgainstLibraryAsync(
        GccCompetitorExtractionDocument extraction,
        IGeekCrawlerRagClient rag,
        string? rawBriefJson,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(rag);
        if (!rag.IsEnabled) return extraction;

        var overrides = GccV2SourceRightsGate.ParseBriefOverrides(rawBriefJson);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> Load(string? pageId, string? runId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) return null;
            var key = $"{runId}|{pageId}";
            if (cache.TryGetValue(key, out var cached)) return cached;
            var page = await rag.GetPageMarkdownAsync(pageId, ct, runId).ConfigureAwait(false);
            cache[key] = page?.Markdown;
            return page?.Markdown;
        }

        async Task<GccPartnerExtractionProvenance> Stamp(
            GccPartnerExtractionProvenance provenance,
            string quote)
        {
            var md = await Load(provenance.PageId, provenance.RunId).ConfigureAwait(false);
            var rights = ResolveRights(provenance, overrides);
            if (string.IsNullOrWhiteSpace(md)
                || !GccV2ToolResearchExtractor.IsVerbatimFromPage(quote, md))
            {
                return provenance with
                {
                    CrawlType = GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                    Quote = quote,
                    SourceRights = rights,
                    MarkdownVerified = false,
                };
            }

            var (start, end) = GccV2PartnerExtractionVerify.FindQuoteOffsets(md, quote);
            var digest = string.IsNullOrWhiteSpace(provenance.SourceDigest)
                ? GccV2PartnerExtractionVerify.ComputeSourceDigest(md)
                : provenance.SourceDigest;
            return provenance with
            {
                CrawlType = GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                Quote = quote,
                StartChar = start,
                EndChar = end,
                SourceRights = rights,
                MarkdownVerified = true,
                SourceDigest = digest,
            };
        }

        var faqs = new List<GccCompetitorFaqAsset>();
        foreach (var f in extraction.FaqBank)
            faqs.Add(f with { Provenance = await Stamp(f.Provenance, f.VerifiedAnswer).ConfigureAwait(false) });

        var proofs = new List<GccCompetitorProofAsset>();
        foreach (var p in extraction.ProofPack)
            proofs.Add(p with { Provenance = await Stamp(p.Provenance, p.ProofClaim).ConfigureAwait(false) });

        var deficits = new List<GccCompetitorDeficitRouterAsset>();
        foreach (var d in extraction.DeficitRouter)
            deficits.Add(d with { Provenance = await Stamp(d.Provenance, d.TriggerDeficit).ConfigureAwait(false) });

        var claimRisk = new List<GccCompetitorClaimRiskAsset>();
        foreach (var c in extraction.ClaimRiskFlags)
            claimRisk.Add(c with { Provenance = await Stamp(c.Provenance, c.ClaimText).ConfigureAwait(false) });

        var framing = new List<GccCompetitorFramingAsset>();
        foreach (var f in extraction.FramingBank)
            framing.Add(f with { Provenance = await Stamp(f.Provenance, f.FrameExcerpt).ConfigureAwait(false) });

        return extraction with
        {
            FaqBank = faqs,
            ProofPack = proofs,
            DeficitRouter = deficits,
            ClaimRiskFlags = claimRisk,
            FramingBank = framing,
        };
    }

    private static string ResolveRights(
        GccPartnerExtractionProvenance provenance,
        IReadOnlyDictionary<string, string> overrides)
    {
        var pageId = (provenance.PageId ?? "").Trim();
        var runId = (provenance.RunId ?? "").Trim();
        if (overrides.Count > 0)
        {
            if (pageId.Length > 0 && runId.Length > 0
                && overrides.TryGetValue($"{runId}|{pageId}", out var both))
                return GccV2SourceRightsGate.Normalize(both);
            if (pageId.Length > 0 && overrides.TryGetValue(pageId, out var byPage))
                return GccV2SourceRightsGate.Normalize(byPage);
            if (runId.Length > 0 && overrides.TryGetValue(runId, out var byRun))
                return GccV2SourceRightsGate.Normalize(byRun);
        }

        return GccV2SourceRightsGate.Normalize(provenance.SourceRights);
    }
}
