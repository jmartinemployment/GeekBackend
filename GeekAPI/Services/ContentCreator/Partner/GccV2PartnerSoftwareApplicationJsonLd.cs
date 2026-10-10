using System.Globalization;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Builds partner <c>SoftwareApplication</c> JSON-LD from extraction payloads (partner-extraction §9).
/// Fail closed on price / review claims without re-verifiable library-backed fields — never invent.
/// </summary>
public static class GccV2PartnerSoftwareApplicationJsonLd
{
    public const string ReviewAuthorName = "Master RAG Pipeline Audit";

    /// <summary>
    /// Returns a schema.org SoftwareApplication object dictionary, or null when identity cannot be grounded.
    /// Price / offers / review are omitted unless extraction supplies verified fields.
    /// </summary>
    public static Dictionary<string, object?>? TryBuild(
        GccPartnerExtractionDocument extraction,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) return null;

        var name = ResolveName(pages);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var node = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "SoftwareApplication",
            ["name"] = name.Trim(),
        };

        var category = extraction.Categories.FirstOrDefault()?.PrimaryCategory;
        node["applicationCategory"] = string.IsNullOrWhiteSpace(category)
            ? "BusinessApplication"
            : category.Trim();

        var os = ResolveOperatingSystem(extraction);
        if (!string.IsNullOrWhiteSpace(os))
            node["operatingSystem"] = os;

        var description = extraction.Advertisements.FirstOrDefault()?.MarketingHook
            ?? pages.SelectMany(p => p.Paragraphs).FirstOrDefault(p => p.Length is >= 24 and <= 280);
        if (!string.IsNullOrWhiteSpace(description))
            node["description"] = description.Trim();

        var offer = TryBuildOffer(extraction);
        if (offer is not null)
            node["offers"] = offer;

        var review = TryBuildReview(extraction);
        if (review is not null)
            node["review"] = review;

        return node;
    }

    /// <summary>Serialize JSON-LD when <see cref="TryBuild"/> succeeds; otherwise null (caller must not invent).</summary>
    public static string? TrySerialize(
        GccPartnerExtractionDocument extraction,
        IReadOnlyList<GccQuoteablePage> pages,
        JsonSerializerOptions? options = null)
    {
        var node = TryBuild(extraction, pages);
        if (node is null) return null;
        return JsonSerializer.Serialize(
            node,
            options ?? new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Throws when ship-ready schema is required and price is asserted without pricing evidence.
    /// Identity-only nodes (name + category) are allowed without offers.
    /// </summary>
    public static void EnsureShipReadyOrThrow(
        Dictionary<string, object?> node,
        GccPartnerExtractionDocument extraction)
    {
        if (!node.TryGetValue("offers", out var offersObj) || offersObj is null)
            return;

        if (offersObj is not Dictionary<string, object?> offer)
            throw new InvalidOperationException(
                "Partner SoftwareApplication offers must be a structured Offer object from library extraction.");

        if (offer.TryGetValue("price", out var price) && price is not null)
        {
            if (extraction.PricingCatalog.All(p => p.ListPrice is null))
            {
                throw new InvalidOperationException(
                    "Partner SoftwareApplication asserts offers.price without Pricing catalog list_price evidence.");
            }
        }
    }

    private static string ResolveName(IReadOnlyList<GccQuoteablePage> pages)
    {
        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.Title))
                return page.Title.Trim();
        }

        return "";
    }

    private static string? ResolveOperatingSystem(GccPartnerExtractionDocument extraction)
    {
        foreach (var comparison in extraction.Comparisons)
        {
            if (comparison.CapabilityPayload.Contains("cloud", StringComparison.OrdinalIgnoreCase)
                || comparison.CapabilityPayload.Contains("web", StringComparison.OrdinalIgnoreCase))
                return "All Cloud Platforms";
        }

        foreach (var integration in extraction.Integrations)
        {
            if (!string.IsNullOrWhiteSpace(integration.ApiOrSdk))
                return "All Cloud Platforms";
        }

        return "Web";
    }

    private static Dictionary<string, object?>? TryBuildOffer(GccPartnerExtractionDocument extraction)
    {
        var priced = extraction.PricingCatalog.FirstOrDefault(p => p.ListPrice is not null);
        var cta = extraction.OfferCtas.FirstOrDefault();

        if (priced is null && cta is null)
            return null;

        var offer = new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
        };

        if (priced?.ListPrice is { } listPrice)
        {
            offer["price"] = listPrice.ToString("0.00", CultureInfo.InvariantCulture);
            offer["priceCurrency"] = string.IsNullOrWhiteSpace(priced.PriceCurrency)
                ? "USD"
                : priced.PriceCurrency;
        }

        if (cta is not null && !string.IsNullOrWhiteSpace(cta.DestinationUrl))
            offer["url"] = cta.DestinationUrl;
        else if (priced is not null)
            offer["url"] = priced.OriginProofUrl;

        var overage = priced?.OverageTerms
            ?? extraction.Comparisons.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.NormalizedCost))
                ?.NormalizedCost;
        if (!string.IsNullOrWhiteSpace(overage))
        {
            var unit = new Dictionary<string, object?>
            {
                ["@type"] = "UnitPriceSpecification",
                ["description"] = overage.Trim(),
            };

            var unitMoney = System.Text.RegularExpressions.Regex.Match(
                overage,
                @"(\d+(?:\.\d+)?)");
            if (unitMoney.Success
                && decimal.TryParse(unitMoney.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice))
            {
                unit["price"] = unitPrice.ToString("0.00", CultureInfo.InvariantCulture);
            }

            offer["priceSpecification"] = unit;
        }

        // Fail closed: do not emit a price key without catalog list price.
        if (offer.ContainsKey("price") && priced?.ListPrice is null)
            return null;

        return offer;
    }

    private static Dictionary<string, object?>? TryBuildReview(GccPartnerExtractionDocument extraction)
    {
        var strengths = extraction.Citables.Select(c => c.IsolatedClaim).Take(2).ToList();
        var limits = extraction.Disqualifiers.Select(d => d.LimitDetail).Take(2).ToList();
        if (strengths.Count == 0 && limits.Count == 0)
        {
            var alt = extraction.Alternatives.FirstOrDefault();
            if (alt is not null)
                limits.Add(alt.TriggerDeficit);
        }

        if (strengths.Count == 0 && limits.Count == 0)
            return null;

        var bodyParts = new List<string>();
        if (strengths.Count > 0)
            bodyParts.Add(string.Join(" ", strengths));
        if (limits.Count > 0)
            bodyParts.Add("Known limits: " + string.Join(" ", limits));

        var body = string.Join(" ", bodyParts).Trim();
        if (body.Length < 24) return null;

        return new Dictionary<string, object?>
        {
            ["@type"] = "Review",
            ["reviewBody"] = body.Length > 500 ? body[..500].TrimEnd() + "…" : body,
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = ReviewAuthorName,
            },
        };
    }
}
