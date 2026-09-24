using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace GeekBackend.IntegrationTests;

public sealed class GeekCrawlerE2ETests : IClassFixture<GeekApiTestFactory>
{
    private readonly GeekApiTestFactory _factory;

    public GeekCrawlerE2ETests(GeekApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Ingest_preserves_links_ownership_and_triggers_index()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        using var create = await owner.PostAsJsonAsync(
            "/api/geek-crawler/ingest/runs",
            new { crawlType = "partner", seeds = new[] { "https://fixture.test/" } });
        create.EnsureSuccessStatusCode();
        var runId = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("run").GetProperty("runId").GetGuid();

        const string contentHtml = "<h1>Exact article</h1><p>A citation-safe paragraph.</p>";
        using var pages = await owner.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/pages/batch",
            new
            {
                pages = new[]
                {
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/article",
                        finalUrl = "https://fixture.test/article",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = "<h1>Exact article</h1>",
                        title = "Exact article",
                        contentHtml,
                        blocks = ArticleBlocks("Exact article", "A citation-safe paragraph."),
                        excerpt = "A citation-safe paragraph.",
                    },
                },
            });
        pages.EnsureSuccessStatusCode();
        var pageId = (await JsonDocument.ParseAsync(await pages.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("pages")[0].GetProperty("pageId").GetGuid();

        using var links = await owner.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/links/batch",
            new
            {
                links = new[]
                {
                    new
                    {
                        pageId,
                        fromUrl = "https://fixture.test/article",
                        linkUrl = "https://fixture.test/contact",
                        isSameOrigin = true,
                    },
                },
            });
        links.EnsureSuccessStatusCode();

        using var other = _factory.CreateAuthenticatedClient(GeekApiTestFactory.OtherUserId);
        using var denied = await other.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/pages/batch",
            new
            {
                pages = new[]
                {
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/private",
                        statusCode = 200,
                        robotsAllowed = true,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var contentReadyAt = DateTimeOffset.UtcNow;
        using var complete = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new
            {
                status = "complete",
                completedAtUtc = contentReadyAt,
                contentReadyAt,
            });
        complete.EnsureSuccessStatusCode();
        var completeSnapshot = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            contentReadyAt,
            completeSnapshot.GetProperty("contentReadyAt").GetDateTimeOffset());

        Assert.Equal(contentHtml, Assert.Single(_factory.Repository.Pages(runId)).ContentHtml);
        Assert.Equal("https://fixture.test/contact", Assert.Single(_factory.Repository.Links(runId)).LinkUrl);
        await EventuallyAsync(() =>
            _factory.Rag.Requests.Any(r => r.Method == HttpMethod.Post && r.Path == "/v1/index"));

        using var resumed = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new { status = "external", clearContentReadyAt = true });
        resumed.EnsureSuccessStatusCode();
        var resumedSnapshot = await resumed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, resumedSnapshot.GetProperty("contentReadyAt").ValueKind);

        using var invalidReadiness = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new
            {
                status = "complete",
                contentReadyAt = DateTimeOffset.UtcNow,
                clearContentReadyAt = true,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReadiness.StatusCode);
    }

    /// <summary>
    /// Soft drops are for pages that legitimately have no body: robots-disallowed, and a fetch that
    /// failed. Those are counted and not persisted, and the batch still succeeds.
    ///
    /// <para>
    /// A page that is allowed and fetched but carries no extracted content is not a soft drop -- it
    /// fails the whole batch closed, which
    /// <see cref="Page_batch_with_blank_content_fails_closed_with_bad_request"/> covers. This test
    /// used to assert the opposite: that an html-only page and a contentHtml-without-blocks page
    /// were both accepted and a blank one was quietly counted. That was the pre-2026-09-18 contract,
    /// and honouring it is how 5,274 pages were reported saved and then deleted for having no body.
    /// </para>
    ///
    /// <para>
    /// "Single format" now means the one canonical corpus format -- contentHtml plus typed blocks.
    /// The second page carries no raw html at all, which is what proves html is not required for a
    /// page to be preserved.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Page_batch_soft_drops_unusable_items_and_preserves_single_format_content()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        var runId = await CreateRunAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/pages/batch",
            new
            {
                pages = new object[]
                {
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/html-and-extract",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = "<main>HTML and extract</main>",
                        contentHtml = "<p>HTML and extract</p>",
                        blocks = ArticleBlocks("HTML and extract", "HTML and extract"),
                    },
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/extract-only",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = (string?)null,
                        contentHtml = "<p>Extracted only</p>",
                        blocks = ArticleBlocks("Extracted only", "Extracted only"),
                    },
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/robots-denied",
                        statusCode = 200,
                        robotsAllowed = false,
                        html = "<main>Must not persist</main>",
                    },
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/request-failed",
                        statusCode = 500,
                        robotsAllowed = true,
                        html = "<main>Must not persist</main>",
                        failureReason = "request failed",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("count").GetInt32());
        Assert.Equal(2, body.GetProperty("pages").GetArrayLength());
        Assert.Equal(2, body.GetProperty("rejectedCount").GetInt32());
        var reasons = body.GetProperty("rejectedReasonCounts");
        Assert.Equal(1, reasons.GetProperty("robotsDisallowed").GetInt32());
        Assert.Equal(1, reasons.GetProperty("failureReason").GetInt32());
        // Still reported, and now necessarily zero on any successful batch: a non-zero blankContent
        // is a 400, never a soft drop. Asserted rather than dropped so the day it can be non-zero
        // here is the day this line fails.
        Assert.Equal(0, reasons.GetProperty("blankContent").GetInt32());
        Assert.Equal(
            body.GetProperty("rejectedCount").GetInt32(),
            reasons.EnumerateObject().Sum(reason => reason.Value.GetInt32()));

        var stored = _factory.Repository.Pages(runId);
        Assert.Equal(2, stored.Count);
        Assert.Contains(
            stored,
            page => page.Url == "https://fixture.test/html-and-extract"
                && page.Html is not null
                && page.ContentHtml is not null);
        Assert.Contains(
            stored,
            page => page.Url == "https://fixture.test/extract-only"
                && page.Html is null
                && page.ContentHtml is not null);
        Assert.DoesNotContain(stored, page => page.Url.Contains("robots-denied", StringComparison.Ordinal));
        Assert.DoesNotContain(stored, page => page.Url.Contains("request-failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The guard that replaced the soft drop. A page that was allowed and fetched but carries no
    /// extracted content fails the entire batch, and nothing is persisted -- not the offending page
    /// and not the good page beside it. Partial acceptance is what made the old behaviour dangerous:
    /// the run went on to complete with a smaller corpus and no record of why.
    /// </summary>
    [Fact]
    public async Task Page_batch_with_blank_content_fails_closed_with_bad_request()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        var runId = await CreateRunAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/pages/batch",
            new
            {
                pages = new object[]
                {
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/good-page",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = "<main>Good</main>",
                        contentHtml = "<p>Good</p>",
                        blocks = ArticleBlocks("Good", "Good"),
                    },
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/blank",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = " ",
                        contentHtml = "\n",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("no extracted content", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://fixture.test/blank", error, StringComparison.Ordinal);

        // The whole batch is refused, so the good page beside the blank one is not stored either.
        Assert.Empty(_factory.Repository.Pages(runId));
    }

    [Fact]
    public async Task Page_batch_returns_ok_with_empty_legacy_result_when_all_items_are_rejected()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        var runId = await CreateRunAsync(owner);

        using var response = await owner.PostAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}/pages/batch",
            new
            {
                pages = new[]
                {
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/denied-and-empty",
                        statusCode = 403,
                        robotsAllowed = false,
                        html = (string?)null,
                    },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
        Assert.Empty(body.GetProperty("pages").EnumerateArray());
        Assert.Equal(1, body.GetProperty("rejectedCount").GetInt32());
        var reasons = body.GetProperty("rejectedReasonCounts");
        Assert.Equal(1, reasons.GetProperty("robotsDisallowed").GetInt32());
        Assert.Equal(0, reasons.GetProperty("failureReason").GetInt32());
        Assert.Equal(0, reasons.GetProperty("blankContent").GetInt32());
        Assert.Empty(_factory.Repository.Pages(runId));
    }

    [Fact]
    public async Task Rag_webhook_is_delivered_to_owner_joined_run_group()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        using var create = await owner.PostAsJsonAsync(
            "/api/geek-crawler/ingest/runs",
            new { crawlType = "partner", seeds = new[] { "https://fixture.test/" } });
        create.EnsureSuccessStatusCode();
        var runId = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("run").GetProperty("runId").GetGuid();

        var received = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "/hubs/geek-crawler-realtime"),
                options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(GeekApiTestFactory.OwnerUserId.ToString("D"));
                    options.Headers["X-API-Key"] = "integration-test-backend-key";
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
            .Build();
        connection.On<JsonElement>("GeekCrawlerRagIndexEvent", payload => received.TrySetResult(payload));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinGeekCrawlerRun", runId);

        using var webhook = _factory.CreateClient();
        webhook.DefaultRequestHeaders.Add("X-API-Key", "integration-test-backend-key");
        using var response = await webhook.PostAsJsonAsync(
            "/api/geek-crawler/internal/rag/index-status",
            new
            {
                runId = runId.ToString("D"),
                state = "complete",
                pagesSeen = 1,
                pagesEnglish = 1,
                chunksUpserted = 2,
            });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("rag_index", payload.GetProperty("eventType").GetString());
        Assert.Equal(runId.ToString("D"), payload.GetProperty("runId").GetString());
        Assert.Equal("complete", payload.GetProperty("state").GetString());
    }

    /// <summary>
    /// Typed blocks in the shape the crawler emits: kind/level/text/html/anchors per block
    /// (Geek-Crawler-v2 extract-content.ts). Ingest fails closed on contentHtml + a non-empty
    /// blocks array (GeekCrawlerIngestController.HasExtractedContent), so any fixture page that
    /// claims content has to carry both.
    /// </summary>
    private static object[] ArticleBlocks(string heading, string paragraph) =>
    [
        new
        {
            kind = "heading",
            level = 1,
            text = heading,
            html = $"<h1>{heading}</h1>",
            anchors = Array.Empty<object>(),
        },
        new
        {
            kind = "paragraph",
            text = paragraph,
            html = $"<p>{paragraph}</p>",
            anchors = Array.Empty<object>(),
        },
    ];

    private static async Task<Guid> CreateRunAsync(HttpClient owner)
    {
        using var create = await owner.PostAsJsonAsync(
            "/api/geek-crawler/ingest/runs",
            new { crawlType = "partner", seeds = new[] { "https://fixture.test/" } });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        // The create response wraps the snapshot: { run, seedsAccepted, rejected }. Rejected seeds
        // travel with the run, so the id sits under "run" rather than at the root.
        return body.GetProperty("run").GetProperty("runId").GetGuid();
    }

    private static async Task EventuallyAsync(Func<bool> assertion)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!assertion() && DateTime.UtcNow < timeout)
            await Task.Delay(20);
        Assert.True(assertion(), "Expected asynchronous protocol request was not observed.");
    }
}
