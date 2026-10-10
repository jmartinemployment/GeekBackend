namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

public sealed record GccV2HeadingLink(string Text, string Href, string Rel = "");

public sealed record GccV2HeadingNode(
    int Level,
    string HeadingText,
    IReadOnlyList<string> Paragraphs,
    IReadOnlyList<GccV2HeadingLink> Links,
    IReadOnlyList<GccV2HeadingNode> Children);

public sealed record GccV2PageHierarchy(
    string PageUrl,
    IReadOnlyList<GccV2HeadingNode> Roots);

public sealed record GccV2SiteHierarchy(
    string HomepageUrl,
    string Viewport,
    DateTimeOffset BuiltAtUtc,
    IReadOnlyList<GccV2PageHierarchy> Pages);

/// <summary>Result of a single-page mobile fetch (homepage now; BFS later).</summary>
public sealed record GccV2FetchedPage(
    string RequestedUrl,
    string FinalUrl,
    string Html,
    int StatusCode,
    IReadOnlyList<string> SameOriginLinks);
