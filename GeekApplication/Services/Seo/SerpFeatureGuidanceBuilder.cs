using GeekApplication.Models.Seo;

namespace GeekApplication.Services.Seo;

public static class SerpFeatureGuidanceBuilder
{
    public static IReadOnlyList<SerpFeatureGuidance> Build(SerpFeatures features)
    {
        var list = new List<SerpFeatureGuidance>();
        if (features.HasFeaturedSnippet)
        {
            list.Add(new SerpFeatureGuidance
            {
                Feature = "featured_snippet",
                ActionText = "Featured snippet detected — add a 40–60 word direct answer in a paragraph immediately after your first H2.",
            });
        }
        if (features.HasPeopleAlsoAsk)
        {
            list.Add(new SerpFeatureGuidance
            {
                Feature = "people_also_ask",
                ActionText = "People Also Ask is present — add H2 questions that match PAA topics and answer each in 2–3 sentences.",
            });
        }
        if (features.HasLocalPack)
        {
            list.Add(new SerpFeatureGuidance
            {
                Feature = "local_pack",
                ActionText = "Local pack detected — include city/region in title and add NAP or service-area details.",
            });
        }
        if (features.HasImagePack)
        {
            list.Add(new SerpFeatureGuidance
            {
                Feature = "image_pack",
                ActionText = "Image pack detected — add descriptive alt text on 2+ relevant images.",
            });
        }
        return list;
    }
}
