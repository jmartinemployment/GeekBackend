using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Plan;

/// <summary>
/// PLAN routing for competitor type labels (competitor-extraction §2 / §6.4):
/// content rivals must not be treated as product substitutes.
/// </summary>
public static class GccV2CompetitorTypePlanRouting
{
    public sealed record RouteResult(
        IReadOnlyList<string> ProductRivalNames,
        IReadOnlyList<string> ContentOnlyRivalNames,
        string GuidanceBlock);

    public static RouteResult Route(GccCompetitorExtractionDocument? extraction)
    {
        if (extraction is null || extraction.TypeLabels.Count == 0)
        {
            return new RouteResult([], [], "");
        }

        var product = new List<string>();
        var contentOnly = new List<string>();
        foreach (var label in extraction.TypeLabels)
        {
            var name = (label.EntityName ?? "").Trim();
            if (name.Length == 0) continue;
            var type = (label.CompetitorType ?? "").Trim().ToLowerInvariant();
            if (type is "content")
            {
                if (!contentOnly.Contains(name, StringComparer.OrdinalIgnoreCase))
                    contentOnly.Add(name);
            }
            else
            {
                // direct | both | unknown → may appear in product-compare framing
                if (!product.Contains(name, StringComparer.OrdinalIgnoreCase))
                    product.Add(name);
            }
        }

        var lines = new List<string>
        {
            "COMPETITOR TYPE ROUTING (required):",
            "- Never treat content-only rivals as product substitutes, recommended tools, or Versus product options.",
            "- Direct/both rivals may appear in honest compare/alternatives framing (crawlType=competitors only).",
        };
        if (contentOnly.Count > 0)
            lines.Add("- Content-only rivals (analysis/SEO coverage only): " + string.Join(", ", contentOnly));
        if (product.Count > 0)
            lines.Add("- Direct/both product rivals: " + string.Join(", ", product));

        return new RouteResult(product, contentOnly, string.Join("\n", lines));
    }

    /// <summary>Drop content-only rival names from entity lists used as product targets.</summary>
    public static IReadOnlyList<string> FilterProductEntities(
        IEnumerable<string> entities,
        IReadOnlyList<string> contentOnlyRivalNames)
    {
        if (contentOnlyRivalNames.Count == 0)
            return entities.ToList();
        return entities
            .Where(e => !contentOnlyRivalNames.Any(c =>
                string.Equals(e.Trim(), c, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
