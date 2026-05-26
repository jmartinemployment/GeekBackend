using GeekApplication.Models.Seo;

namespace GeekApplication.Services.Seo;

public static class EeatAdvisoryBuilder
{
    private static readonly string[] ExperiencePhrases =
        ["we installed", "our team", "i tested", "in our experience", "we found that"];

    public static IReadOnlyList<EeatAdvisory> Build(string plainText, string contentHtml)
    {
        var advisories = new List<EeatAdvisory>();
        var lower = plainText.ToLowerInvariant();

        if (!ExperiencePhrases.Any(p => lower.Contains(p, StringComparison.Ordinal)))
        {
            advisories.Add(new EeatAdvisory
            {
                Code = "first_hand_experience",
                ActionText = "Add a short section describing first-hand experience with this topic.",
            });
        }

        if (!contentHtml.Contains("schema.org", StringComparison.OrdinalIgnoreCase)
            && !contentHtml.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase))
        {
            advisories.Add(new EeatAdvisory
            {
                Code = "author_schema",
                ActionText = "Add Article schema with an author property to strengthen trust signals.",
            });
        }

        return advisories;
    }
}
