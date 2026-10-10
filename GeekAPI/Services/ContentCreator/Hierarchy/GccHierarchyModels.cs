namespace GeekAPI.Services.ContentCreator.Hierarchy;

public sealed record GccHeadingLink(string Text, string Href, string Rel = "");

public sealed record GccHeadingNode(
    int Level,
    string HeadingText,
    IReadOnlyList<string> Paragraphs,
    IReadOnlyList<GccHeadingLink> Links,
    IReadOnlyList<GccHeadingNode> Children);

public sealed record GccPageHierarchy(
    string PageUrl,
    IReadOnlyList<GccHeadingNode> Roots);
