using System.Globalization;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Competitor <c>SoftwareApplication</c> JSON-LD (competitor-extraction §10).
/// Always analysis-only — never sellable partner schema; crawlType stays competitors.
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
        var cta = extraction.OfferCtas.FirstOrDefault();
        if (priced is not null || cta is not null)
        {
            var offer = new Dictionary<string, object?> { ["@type"] = "Offer" };
            if (priced?.ListPrice is { } listPrice)
            {
                offer["price"] = listPrice.ToString("0.00", CultureInfo.InvariantCulture);
                offer["priceCurrency"] = string.IsNullOrWhiteSpace(priced.PriceCurrency)
                    ? "USD"
                    : priced.PriceCurrency;
            }

            if (cta is not null)
                offer["url"] = cta.DestinationUrl;
            else if (priced is not null)
                offer["url"] = priced.OriginProofUrl;

            if (!string.IsNullOrWhiteSpace(priced?.OverageTerms))
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
