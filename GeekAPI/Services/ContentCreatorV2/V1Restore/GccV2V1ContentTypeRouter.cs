namespace GeekAPI.Services.ContentCreatorV2.V1Restore;

/// <summary>
/// Which v1 generator serves a Create's content type.
///
/// v1 does not have a generator for every type the Create form offers. Where it has none, the job
/// fails saying so rather than quietly running the pillar generator and returning something of the
/// wrong shape - a long-form article standing in for a social post is exactly the kind of
/// success-shaped wrong answer this restore exists to remove.
/// </summary>
public enum GccV2V1Generator
{
    /// <summary>Long-form article — v1 GeneratePillarPlanAsync / GeneratePillarBodyAsync.</summary>
    Pillar,
    /// <summary>v1 GenerateBlogAsync.</summary>
    Blog,
    /// <summary>v1 GenerateToolPagesAsync.</summary>
    ToolPage,
    /// <summary>No v1 generator produces a document body for this type.</summary>
    Unsupported,
}

public static class GccV2V1ContentTypeRouter
{
    /// <summary>
    /// Long-form types all route to v1's pillar generator. They differ in brief and outline shape,
    /// not in the generator that writes them — v1 has one long-form writer.
    /// </summary>
    private static readonly HashSet<string> LongForm = new(StringComparer.OrdinalIgnoreCase)
    {
        "pillar", "tech-article", "comparison", "guide", "alternatives",
        "listicle", "case-study", "service", "local", "whitepaper",
    };

    public static GccV2V1Generator For(string? contentType)
    {
        var type = (contentType ?? "").Trim().ToLowerInvariant();
        if (type.Length == 0) return GccV2V1Generator.Pillar;
        if (LongForm.Contains(type)) return GccV2V1Generator.Pillar;
        return type switch
        {
            "blog" => GccV2V1Generator.Blog,
            "tool" => GccV2V1Generator.ToolPage,
            _ => GccV2V1Generator.Unsupported,
        };
    }

    /// <summary>Message for a type v1 cannot write, naming the type rather than failing vaguely.</summary>
    public static string UnsupportedMessage(string? contentType) =>
        $"v1 has no generator that produces a document body for content type '{contentType}'. "
        + "Supported: long-form (pillar, tech-article, comparison, guide, alternatives, listicle, "
        + "case-study, service, local, whitepaper), blog, and tool. This job is not being written by "
        + "the pillar generator as a substitute.";
}
