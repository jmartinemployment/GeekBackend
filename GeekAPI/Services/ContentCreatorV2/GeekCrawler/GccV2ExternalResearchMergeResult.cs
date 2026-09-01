namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

public sealed record GccV2ExternalResearchMergeResult(
    string? BriefJson,
    IReadOnlyList<string> PartnerResearchWarnings);
