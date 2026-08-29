namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>Thrown when an overview tool job must wait for its pillar sibling or partner spawn.</summary>
public sealed class GccV2ToolWriteDeferredException : Exception
{
    public GccV2ToolWriteDeferredException(string message) : base(message) { }
}

public sealed record GccV2ToolPageWriteExtras(
    string Kind,
    string Slug,
    string? JsonLdSchema,
    List<string>? Keywords,
    string? Excerpt,
    string? MainSummary,
    string? HeroSummary,
    string? HomeSummary,
    string? BlogSummary,
    string? AdvertisingSummary,
    string? SourceAttributionHtml,
    string? PillarArticleUrl);
