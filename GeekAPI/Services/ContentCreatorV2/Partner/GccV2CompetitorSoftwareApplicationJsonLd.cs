using System.Globalization;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Competitor <c>SoftwareApplication</c> JSON-LD (competitor-extraction §10).
/// Always analysis-only — never sellable partner schema; crawlType stays competitors.
/// A competitor is never given an <c>offers.url</c> CTA/destination — there is no
/// <see cref="GccCompetitorOfferCtaAsset"/>; the type no longer exists (see
/// plans/rag-foundation-rewrite.md §0.000/§3, plans/competitor-extraction-complete.md).
/// TODO: this builder still emits <c>@type: SoftwareApplication</c> + <c>Offer</c>/<c>priceCurrency</c>
/// for a rival business, which is itself a category error for a services/agency competitor
/// (Organization/ProfessionalService is correct) — tracked, not fixed in this pass; see §0.000.
/// </summary>
public static class GccV2CompetitorSoftwareApplicationJsonLd
{
    public const string ReviewAuthorName = "Master RAG Pipeline Audit";

    public static Dictionary<string, object?>? TryBuild(
        GccCompetitorExtractionDocument extraction,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) return null;

        var name = pages.Select(p => p.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                   ?? extraction.TypeLabels.FirstOrDefault()?.EntityName;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var url = extraction.TypeLabels.FirstOrDefault()?.PrimaryUrl
                  ?? pages[0].Url;
        var description = extraction.DemandSignals.FirstOrDefault()?.AdOrCopyTheme
                          ?? extraction.TypeLabels.FirstOrDefault()?.TypeRationale
                          ?? pages.SelectMany(p => p.Paragraphs).FirstOrDefault(p => p.Length is >= 24 and <= 280);

        var node = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "SoftwareApplication",
            ["name"] = name.Trim(),
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web Browser",
            ["url"] = url,
        };
        if (!string.IsNullOrWhiteSpace(description))
            node["description"] = description.Trim();

        var priced = extraction.PricingCatalog.FirstOrDefault(p => p.ListPrice is not null);
        if (priced is not null)
        {
            var offer = new Dictionary<string, object?> { ["@type"] = "Offer" };
            if (priced.ListPrice is { } listPrice)
            {
                offer["price"] = listPrice.ToString("0.00", CultureInfo.InvariantCulture);
                offer["priceCurrency"] = string.IsNullOrWhiteSpace(priced.PriceCurrency)
                    ? "USD"
                    : priced.PriceCurrency;
            }

            // No CTA/destination is ever emitted for a competitor — offer.url points at the
            // source evidence page, never a conversion path to the rival (§0.000).
            offer["url"] = priced.OriginProofUrl;

            if (!string.IsNullOrWhiteSpace(priced.OverageTerms))
            {
                offer["priceSpecification"] = new Dictionary<string, object?>
                {
                    ["@type"] = "UnitPriceSpecification",
                    ["description"] = priced.OverageTerms,
                };
            }

            if (offer.ContainsKey("price") && priced?.ListPrice is null)
            {
                // fail closed — no price without catalog
            }
            else
            {
                node["offers"] = offer;
            }
        }

        var deficits = extraction.DeficitRouter.Select(d => d.TriggerDeficit)
            .Concat(extraction.Disqualifiers.Select(d => d.LimitDetail))
            .Take(3)
            .ToList();
        if (deficits.Count > 0)
        {
            var body = "CRITICAL DISADVANTAGE: " + string.Join(" ", deficits);
            if (body.Length > 500) body = body[..500].TrimEnd() + "…";
            node["review"] = new Dictionary<string, object?>
            {
                ["@type"] = "Review",
                ["reviewBody"] = body,
                ["itemReviewed"] = new Dictionary<string, object?>
                {
                    ["@type"] = "SoftwareApplication",
                    ["name"] = name.Trim(),
                },
                ["author"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Organization",
                    ["name"] = ReviewAuthorName,
                },
            };
        }

        return node;
    }

    public static void EnsureShipReadyOrThrow(
        Dictionary<string, object?> node,
        GccCompetitorExtractionDocument extraction)
    {
        if (!node.TryGetValue("offers", out var offersObj) || offersObj is null)
            return;
        if (offersObj is not Dictionary<string, object?> offer)
            throw new InvalidOperationException(
                "Competitor SoftwareApplication offers must be a structured Offer from library extraction.");
        if (offer.TryGetValue("price", out var price) && price is not null
            && extraction.PricingCatalog.All(p => p.ListPrice is null))
        {
            throw new InvalidOperationException(
                "Competitor SoftwareApplication asserts offers.price without Pricing catalog list_price evidence.");
        }
    }
}
