extern alias GeekApi;

using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using GeekApi::GeekAPI.HttpClients;
using GeekApi::GeekAPI.Services.GeekCrawler;
using GeekApi::GeekAPI.Services.Rag;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.IntegrationTests;

public sealed class RagClientContractTests : IClassFixture<GeekApiTestFactory>
{
    private readonly GeekApiTestFactory _factory;

    public RagClientContractTests(GeekApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Rag_status_reports_the_library_and_never_offers_generation()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync("/api/rag/status");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        // RAG is retrieval and verification only: the library is there, and it does not generate.
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.True(root.GetProperty("ragClientEnabled").GetBoolean());
        Assert.False(root.GetProperty("generateEnabled").GetBoolean());
        Assert.False(root.GetProperty("citeableGenerateAvailable").GetBoolean());
        Assert.True(root.GetProperty("writingIntents").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Index_query_and_page_follow_protocol_and_propagate_key()
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
        var page = await client.GetPageTextAsync(RagProtocolStubHandler.ArticlePageId);

        Assert.Equal("queued", index?.State);
        Assert.Equal("hybrid", query?.Retrieval);
        var quoteable = Assert.Single(query!.Pages);
        Assert.Equal(RagProtocolStubHandler.ArticlePageId, quoteable.PageId);
        Assert.Equal(RagProtocolStubHandler.ArticleText, page?.Text);
        Assert.Contains(RagProtocolStubHandler.CitationQuote, page!.Text, StringComparison.Ordinal);

        // Binding alone is not the contract -- the structure has to survive the last hop into the
        // text the writer is handed. A retrieved child arrives wrapped in the parent block it sits
        // inside, and the anchors under its heading are named.
        var paragraph = Assert.Single(quoteable.Paragraphs);
        Assert.Contains(
            $"Context: {RagProtocolStubHandler.ChunkParentText}",
            paragraph,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Specific detail: {RagProtocolStubHandler.CitationQuote}",
            paragraph,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Linked from this section: {RagProtocolStubHandler.AnchorPricingLabel}, "
            + RagProtocolStubHandler.AnchorDocsLabel,
            paragraph,
            StringComparison.Ordinal);
        // Hrefs are captured on the DTO for anchor-based tool detection; they are not prompt text.
        Assert.DoesNotContain(RagProtocolStubHandler.AnchorPricingHref, paragraph, StringComparison.Ordinal);

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
        Assert.DoesNotContain(
            _factory.Rag.Requests,
            r => r.Path == "/v1/generate");
    }

    /// <summary>
    /// The cross-service field contract, asserted against the client's own serializer options and
    /// DTO rather than a copy of either.
    ///
    /// <para>
    /// This is the test that was missing when <c>anchors</c> was typed <c>List&lt;string&gt;</c>
    /// against Geek-Crawler-Rag's <c>list[ChunkAnchor]</c>: deserialization threw, QueryAsync's
    /// catch turned the throw into a failed result, and every page carrying a link looked exactly
    /// like RAG being unreachable. Nothing on either side of the wire caught it.
    /// </para>
    /// </summary>
    [Fact]
    public void Query_chunk_payload_binds_parent_child_and_structured_anchors()
    {
        var runId = Guid.NewGuid().ToString("D");
        using var document = JsonDocument.Parse(RagProtocolStubHandler.BuildQueryResponseJson(runId));
        var chunkJson = document.RootElement.GetProperty("chunks")[0].GetRawText();

        // The production options, not a local copy -- a naming-policy or converter change on the
        // client has to fail here rather than pass against a fixture that drifted away from it.
        var chunk = JsonSerializer.Deserialize<HttpGeekCrawlerRagClient.ChunkDto>(
            chunkJson,
            HttpGeekCrawlerRagClient.JsonOpts);

        Assert.NotNull(chunk);
        Assert.Equal(runId, chunk!.RunId);
        Assert.Equal(RagProtocolStubHandler.ChunkRole, chunk.ChunkRole);
        Assert.Equal(RagProtocolStubHandler.ChunkParentText, chunk.ParentText);
        Assert.Equal(RagProtocolStubHandler.ChunkChildText, chunk.ChildText);

        // Objects, not strings: the list binds to the structured anchor DTO and both members of
        // each element are mapped, in payload order.
        Assert.NotNull(chunk.Anchors);
        Assert.Equal(2, chunk.Anchors!.Count);
        Assert.All(chunk.Anchors, anchor =>
            Assert.IsType<HttpGeekCrawlerRagClient.GeekCrawlerRagAnchorDto>(anchor));
        Assert.Equal(RagProtocolStubHandler.AnchorPricingLabel, chunk.Anchors[0].Label);
        Assert.Equal(RagProtocolStubHandler.AnchorPricingHref, chunk.Anchors[0].Href);
        Assert.Equal(RagProtocolStubHandler.AnchorDocsLabel, chunk.Anchors[1].Label);
        Assert.Equal(RagProtocolStubHandler.AnchorDocsHref, chunk.Anchors[1].Href);
    }

    /// <summary>
    /// The wire names are the camelCase aliases Geek-Crawler-Rag serializes
    /// (<c>ser_json_by_alias</c>), not the snake_case field names of its Python model. Binding is
    /// case-insensitive on this client, so a case difference would pass; a different word shape
    /// must not, or a future rename on the Python side would land here as silent nulls.
    /// </summary>
    [Fact]
    public void Query_chunk_payload_does_not_bind_python_field_names()
    {
        const string snakeCaseChunk = """
        {
          "run_id": "11111111-1111-1111-1111-111111111111",
          "url": "https://fixture.test/article",
          "chunk_index": 0,
          "text": "body",
          "chunk_role": "child",
          "parent_text": "the parent block",
          "child_text": "the child span",
          "section_title": "Fixture article"
        }
        """;

        var chunk = JsonSerializer.Deserialize<HttpGeekCrawlerRagClient.ChunkDto>(
            snakeCaseChunk,
            HttpGeekCrawlerRagClient.JsonOpts);

        Assert.NotNull(chunk);
        Assert.Null(chunk!.ParentText);
        Assert.Null(chunk.ChildText);
        Assert.Null(chunk.ChunkRole);
        Assert.Null(chunk.SectionTitle);
        Assert.Equal(0, chunk.ChunkIndex);
        // "text" and "url" are single words, so they bind under either convention -- which is why
        // the fields above are the ones worth asserting.
        Assert.Equal("body", chunk.Text);
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
            Assert.Null(await client.GetPageTextAsync(RagProtocolStubHandler.ArticlePageId));
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
