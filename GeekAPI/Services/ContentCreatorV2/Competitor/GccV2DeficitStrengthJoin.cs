using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Competitor;

/// <summary>
/// The counterweight join: a rival's service gap bound to the partner strength that answers it.
///
/// Previously these two halves were both extracted and never joined — competitor deficits were merged
/// into partner <c>Alternatives</c> as flat text, leaving the model to improvise the pairing from two
/// unrelated lists in its prompt (plans/rag-foundation-rewrite.md §3.2). That is where invention entered.
///
/// The admissibility rule is deliberately strict:
/// a pair survives only when the competitor deficit and the partner strength sit on the <b>same axis id</b>
/// and <b>each side carries its own source chunk id</b>. Two chunk ids and a shared axis, or the pair is
/// dropped. Nothing is inferred to complete a pair.
/// </summary>
public static class GccV2DeficitStrengthJoin
{
    /// <summary>
    /// Returns the competitor document with each deficit either bound to a partner strength on the same
    /// axis, or left unbound. Unbound deficits are retained for transparency but carry no recommended
    /// swap — downstream renders them as "unjoined" rather than inventing one.
    /// </summary>
    public static GccCompetitorExtractionDocument Bind(
        GccCompetitorExtractionDocument competitor,
        GccPartnerExtractionDocument? partner,
        IReadOnlyList<string> partnerNames)
    {
        ArgumentNullException.ThrowIfNull(competitor);
        if (competitor.DeficitRouter.Count == 0) return competitor;
        if (partner is null || partner.Comparisons.Count == 0) return competitor;

        // Partner strengths indexed by axis. StandardizedFeatureId is the partner-side axis key;
        // AxisId is the competitor-side one. They are the same vocabulary by contract.
        var strengthsByAxis = partner.Comparisons
            .Where(c => !string.IsNullOrWhiteSpace(c.StandardizedFeatureId)
                        && !string.IsNullOrWhiteSpace(c.CapabilityPayload)
                        && !string.IsNullOrWhiteSpace(c.Provenance.PageId))
            .GroupBy(c => c.StandardizedFeatureId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (strengthsByAxis.Count == 0) return competitor;

        var swaps = partnerNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bound = new List<GccCompetitorDeficitRouterAsset>(competitor.DeficitRouter.Count);
        foreach (var deficit in competitor.DeficitRouter)
        {
            var axis = (deficit.AxisId ?? "").Trim();
            if (axis.Length == 0
                || string.IsNullOrWhiteSpace(deficit.CompetitorDeficitChunkId)
                || !strengthsByAxis.TryGetValue(axis, out var strength))
            {
                bound.Add(deficit);
                continue;
            }

            bound.Add(deficit with
            {
                RecommendedSwap = swaps,
                PivotCopy = strength.CapabilityPayload,
                PartnerStrengthChunkId = strength.Provenance.PageId,
            });
        }

        return competitor with { DeficitRouter = bound };
    }

    /// <summary>
    /// Deficits that completed the join — the only ones eligible to become partner
    /// <c>Alternatives</c> copy. An unjoined deficit has no evidenced strength to point at.
    /// </summary>
    public static IReadOnlyList<GccCompetitorDeficitRouterAsset> JoinedOnly(
        GccCompetitorExtractionDocument competitor) =>
        competitor.DeficitRouter
            .Where(d => !string.IsNullOrWhiteSpace(d.CompetitorDeficitChunkId)
                        && !string.IsNullOrWhiteSpace(d.PartnerStrengthChunkId)
                        && d.RecommendedSwap.Count > 0)
            .ToList();

    /// <summary>
    /// Explicit competitor-to-partner provenance bridge, used only when a joined competitor deficit
    /// becomes a partner <c>Alternatives</c> row. The crossing is deliberate and visible here rather
    /// than implicit in a shared type — <c>CrawlType</c> stays <c>competitors</c> so downstream gates
    /// can still tell where the claim came from.
    /// </summary>
    public static GccPartnerExtractionProvenance ToPartnerProvenance(
        GccCompetitorExtractionProvenance provenance) =>
        new(
            OriginProofUrl: provenance.OriginProofUrl,
            CrawlType: GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            RunId: provenance.RunId,
            PageId: provenance.PageId,
            SectionTitle: provenance.SectionTitle,
            SourceDigest: provenance.SourceDigest,
            TemporalAnchorUtc: provenance.TemporalAnchorUtc,
            Quote: provenance.Quote,
            StartChar: provenance.StartChar,
            EndChar: provenance.EndChar,
            SectionKey: provenance.SectionKey,
            SourceRights: provenance.SourceRights,
            QuoteVerified: provenance.QuoteVerified);
}
