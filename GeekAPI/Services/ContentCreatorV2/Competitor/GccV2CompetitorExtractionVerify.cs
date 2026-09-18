using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Competitor;

/// <summary>
/// Block-text verify for competitor claim-bearing assets.
///
/// The extractor records the verbatim source span on <see cref="GccCompetitorExtractionProvenance.Quote"/>;
/// this stamps each asset with whether that span is literally present in the source page text, along with
/// its offsets, digest and source rights. Assets that fail verification are kept but stamped
/// <c>QuoteVerified = false</c> — the downstream citation and claim-risk gates decide what may ship.
/// Nothing is repaired or substituted here.
/// </summary>
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
            var page = await rag.GetPageTextAsync(pageId, ct, runId).ConfigureAwait(false);
            cache[key] = page?.Text;
            return page?.Text;
        }

        // Prefer the verbatim span the extractor captured; fall back to the asset's own claim text.
        async Task<GccCompetitorExtractionProvenance> Stamp(
            GccCompetitorExtractionProvenance provenance,
            string fallbackQuote)
        {
            var quote = string.IsNullOrWhiteSpace(provenance.Quote) ? fallbackQuote : provenance.Quote!;
            var pageText = await Load(provenance.PageId, provenance.RunId).ConfigureAwait(false);
            var rights = ResolveRights(provenance, overrides);

            if (string.IsNullOrWhiteSpace(pageText)
                || string.IsNullOrWhiteSpace(quote)
                || !GccV2ToolResearchExtractor.IsVerbatimFromPage(quote, pageText))
            {
                return provenance with
                {
                    CrawlType = GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                    Quote = quote,
                    SourceRights = rights,
                    QuoteVerified = false,
                };
            }

            var (start, end) = GccV2PartnerExtractionVerify.FindQuoteOffsets(pageText, quote);
            var digest = string.IsNullOrWhiteSpace(provenance.SourceDigest)
                ? GccV2PartnerExtractionVerify.ComputeSourceDigest(pageText)
                : provenance.SourceDigest;
            return provenance with
            {
                CrawlType = GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                Quote = quote,
                StartChar = start,
                EndChar = end,
                SourceRights = rights,
                QuoteVerified = true,
                SourceDigest = digest,
            };
        }

        var coverage = new List<GccCompetitorCoverageAsset>();
        foreach (var c in extraction.CoverageMap)
            coverage.Add(c with { Provenance = await Stamp(c.Provenance, c.TopicPath).ConfigureAwait(false) });

        var gaps = new List<GccCompetitorGapMapAsset>();
        foreach (var g in extraction.GapMap)
            gaps.Add(g with { Provenance = await Stamp(g.Provenance, g.OpportunityForUs).ConfigureAwait(false) });

        var axes = new List<GccCompetitorComparisonAxisAsset>();
        foreach (var a in extraction.ComparisonAxes)
            axes.Add(a with { Provenance = await Stamp(a.Provenance, a.RivalCapabilityPayload).ConfigureAwait(false) });

        var deficits = new List<GccCompetitorDeficitRouterAsset>();
        foreach (var d in extraction.DeficitRouter)
            deficits.Add(d with { Provenance = await Stamp(d.Provenance, d.TriggerDeficit).ConfigureAwait(false) });

        var faqs = new List<GccCompetitorFaqAsset>();
        foreach (var f in extraction.FaqBank)
            faqs.Add(f with { Provenance = await Stamp(f.Provenance, f.VerifiedAnswer).ConfigureAwait(false) });

        var proofs = new List<GccCompetitorProofAsset>();
        foreach (var p in extraction.ProofPack)
            proofs.Add(p with { Provenance = await Stamp(p.Provenance, p.ProofClaim).ConfigureAwait(false) });

        var framing = new List<GccCompetitorFramingAsset>();
        foreach (var f in extraction.FramingBank)
            framing.Add(f with { Provenance = await Stamp(f.Provenance, f.FrameExcerpt).ConfigureAwait(false) });

        var claimRisk = new List<GccCompetitorClaimRiskAsset>();
        foreach (var c in extraction.ClaimRiskFlags)
            claimRisk.Add(c with { Provenance = await Stamp(c.Provenance, c.ClaimText).ConfigureAwait(false) });

        var services = new List<GccCompetitorServiceAsset>();
        foreach (var s in extraction.ServiceOfferings)
            services.Add(s with { Provenance = await Stamp(s.Provenance, s.ServiceName).ConfigureAwait(false) });

        var clients = new List<GccCompetitorClientProofAsset>();
        foreach (var c in extraction.NamedClients)
            clients.Add(c with { Provenance = await Stamp(c.Provenance, c.OutcomeClaim ?? c.ClientName).ConfigureAwait(false) });

        var presence = new List<GccCompetitorPresenceAsset>();
        foreach (var p in extraction.GeographicPresence)
            presence.Add(p with { Provenance = await Stamp(p.Provenance, p.Location).ConfigureAwait(false) });

        var credentials = new List<GccCompetitorCredentialAsset>();
        foreach (var c in extraction.TeamCredentials)
            credentials.Add(c with { Provenance = await Stamp(c.Provenance, c.CredentialName).ConfigureAwait(false) });

        var boundaries = new List<GccCompetitorBoundaryAsset>();
        foreach (var b in extraction.Disqualifiers)
            boundaries.Add(b with { Provenance = await Stamp(b.Provenance, b.BoundaryDetail).ConfigureAwait(false) });

        var positioning = new List<GccCompetitorPositioningAsset>();
        foreach (var p in extraction.PositioningStatements)
            positioning.Add(p with { Provenance = await Stamp(p.Provenance, p.PositioningStatement).ConfigureAwait(false) });

        return extraction with
        {
            CoverageMap = coverage,
            GapMap = gaps,
            ComparisonAxes = axes,
            DeficitRouter = deficits,
            FaqBank = faqs,
            ProofPack = proofs,
            FramingBank = framing,
            ClaimRiskFlags = claimRisk,
            ServiceOfferings = services,
            NamedClients = clients,
            GeographicPresence = presence,
            TeamCredentials = credentials,
            PositioningStatements = positioning,
            Disqualifiers = boundaries,
        };
    }

    private static string ResolveRights(
        GccCompetitorExtractionProvenance provenance,
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
