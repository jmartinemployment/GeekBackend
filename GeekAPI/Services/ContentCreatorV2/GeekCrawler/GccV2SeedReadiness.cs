namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

/// <param name="Seed">The URL the operator entered.</param>
/// <param name="Ready">True only when an owned, indexed crawl run exists for this seed.</param>
/// <param name="Reason">Why it is not ready; null when it is.</param>
public sealed record GccV2SeedReadiness(
    string Seed,
    bool Ready,
    Guid? RunId,
    string? IndexState,
    string? Reason);
