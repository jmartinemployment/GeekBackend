namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>Copied from workflow <c>ToolMetadataDraft</c> — v2-owned DTO for partner tool pages.</summary>
public sealed record GccV2ToolMetadataDraft(
    string DepartmentListExcerpt,
    string Summary,
    string MainSummary,
    string HeroSummary,
    string HomeSummary,
    string BlogSummary,
    string ToolPageExcerpt,
    string AdvertisingSummary,
    string MetaDescription);
