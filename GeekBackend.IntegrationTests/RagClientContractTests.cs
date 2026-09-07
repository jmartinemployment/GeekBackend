extern alias GeekApi;

using System.Text.Json;
using GeekApi::GeekAPI.Services.GeekCrawler;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.IntegrationTests;

public sealed class RagClientContractTests : IClassFixture<GeekApiTestFactory>
{
    private readonly GeekApiTestFactory _factory;

    public RagClientContractTests(GeekApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Index_query_page_and_generate_follow_protocol_and_propagate_key()
    {
        var runId = Guid.NewGuid();
        var client = _factory.Services.GetRequiredService<IGeekCrawlerRagClient>();

        var index = await client.EnqueueIndexAsync(runId);
        var query = await client.QueryAsync(
            "citation evidence",
            runId,
            crawlType: "partner",
            topK: 4,
            preferParent: true,
            entityNames: ["Fixture Co"],
            retrievalMode: "hybrid");
        var page = await client.GetPageMarkdownAsync(RagProtocolStubHandler.ArticlePageId);
        var generated = await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "blog",
            Topic = "Deterministic evidence",
            PartnerRunId = runId.ToString("D"),
            TargetEntities = ["Fixture Co"],
        });

        Assert.Equal("queued", index?.State);
        Assert.Equal("hybrid", query?.Retrieval);
        var quoteable = Assert.Single(query!.Pages);
        Assert.Equal(RagProtocolStubHandler.ArticlePageId, quoteable.PageId);
        Assert.Equal(RagProtocolStubHandler.ArticleMarkdown, page?.Markdown);
        var citation = Assert.Single(generated!.Citations);
        Assert.Equal(page!.PageId, citation.PageId);
        Assert.Equal(page.Url, citation.Url);
        Assert.Contains(citation.Quote, page.Markdown, StringComparison.Ordinal);

        Assert.All(
            _factory.Rag.Requests.Where(r => r.Path.StartsWith("/v1/", StringComparison.Ordinal)),
            request => Assert.Equal(
                "integration-test-rag-key",
                request.Headers["X-Api-Key"]));

        var queryRequest = Assert.Single(
            _factory.Rag.Requests,
            r => r.Path == "/v1/query" && r.Body.Contains(runId.ToString("D"), StringComparison.Ordinal));
        using var queryJson = JsonDocument.Parse(queryRequest.Body);
        Assert.True(queryJson.RootElement.GetProperty("preferParent").GetBoolean());
        Assert.Equal("Fixture Co", queryJson.RootElement.GetProperty("entityNames")[0].GetString());
    }

    [Fact]
    public async Task Staged_outline_and_section_requests_preserve_keys_and_context()
    {
        var client = _factory.Services.GetRequiredService<IGeekCrawlerRagClient>();

        var outline = await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "blog",
            Topic = "Staged article",
            GenerationStage = "outline",
        });
        var section = await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "blog",
            Topic = "Staged article",
            GenerationStage = "section",
            Outline =
            [
                new GeekCrawlerRagOutlineSectionDto
                {
                    Key = "section-1",
                    Heading = "Verified claims",
                    Brief = "Use evidence.",
                },
            ],
            SectionKey = "section-1",
            SectionHeading = "Verified claims",
            SectionBrief = "Use evidence.",
            CompletedSectionSummaries = ["Introduction completed."],
        });

        Assert.Equal("section-1", Assert.Single(outline!.Outline!).Key);
        Assert.Equal("A section with a verified claim.", section!.Content);

        var sectionRequest = _factory.Rag.Requests.Last(r =>
            r.Path == "/v1/generate"
            && r.Body.Contains("\"generationStage\":\"section\"", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(sectionRequest.Body);
        var root = json.RootElement;
        Assert.Equal("section-1", root.GetProperty("sectionKey").GetString());
        Assert.Equal("section-1", root.GetProperty("outline")[0].GetProperty("key").GetString());
        Assert.Equal(
            "Introduction completed.",
            root.GetProperty("completedSectionSummaries")[0].GetString());
    }

    [Fact]
    public async Task Upstream_failures_are_soft()
    {
        var client = _factory.Services.GetRequiredService<IGeekCrawlerRagClient>();
        _factory.Rag.FailRequests = true;
        try
        {
            var runId = Guid.NewGuid();
            Assert.Null(await client.EnqueueIndexAsync(runId));
            Assert.Null(await client.GetPageMarkdownAsync(RagProtocolStubHandler.ArticlePageId));
            Assert.Null(await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
            {
                WritingIntent = "blog",
                Topic = "Unavailable",
            }));

            var query = await client.QueryAsync("unavailable", runId);
            Assert.NotNull(query);
            Assert.Empty(query.Pages);
            Assert.Contains("503", query.Warning, StringComparison.Ordinal);
        }
        finally
        {
            _factory.Rag.FailRequests = false;
        }
    }
}
