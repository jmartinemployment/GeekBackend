using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public class GccV2ProjectSiteKnowledgeTests
{
    [Fact]
    public void Promotion_NormalizesOnlySelectedUsablePages_WithProvenance()
    {
        var runId = Guid.NewGuid();
        var selectedId = Guid.NewGuid();
        var excludedId = Guid.NewGuid();
        var pages = new[]
        {
            Page(selectedId, runId, "https://example.com/selected",
                "<html><head><title>Selected page</title></head><body>"
                + "<h1>Evidence</h1><p>This selected paragraph contains useful owned website evidence.</p>"
                + "</body></html>"),
            Page(excludedId, runId, "https://example.com/excluded",
                "<html><body><p>This paragraph should not be included in the promoted source.</p></body></html>"),
        };

        var content = GccV2ProjectSiteKnowledgeService.BuildNormalizedContent(
            pages, new HashSet<Guid> { selectedId }, out var includedIds);

        Assert.Equal([selectedId], includedIds);
        Assert.Contains("Source: https://example.com/selected", content);
        Assert.Contains("Selected page", content);
        Assert.DoesNotContain("should not be included", content);
    }

    [Fact]
    public void Promotion_RejectsRobotsDeniedAndEmptyPages()
    {
        var runId = Guid.NewGuid();
        var denied = Page(Guid.NewGuid(), runId, "https://example.com/denied",
            "<html><body><p>This otherwise useful paragraph is denied.</p></body></html>") with
        {
            RobotsAllowed = false,
        };
        var empty = Page(Guid.NewGuid(), runId, "https://example.com/empty", "<html></html>");

        var content = GccV2ProjectSiteKnowledgeService.BuildNormalizedContent(
            [denied, empty], null, out var includedIds);

        Assert.Empty(content);
        Assert.Empty(includedIds);
    }

    private static GccV2ProjectSiteCrawlPageDto Page(
        Guid id, Guid runId, string url, string html) =>
        new(id, runId, "project-site", url, url, 200, true, html, DateTimeOffset.UtcNow);
}
