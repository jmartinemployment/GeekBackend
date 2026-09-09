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
    public async Task Ingest_preserves_markdown_links_ownership_and_triggers_index()
    {
        using var owner = _factory.CreateAuthenticatedClient();
        using var create = await owner.PostAsJsonAsync(
            "/api/geek-crawler/ingest/runs",
            new { crawlType = "partner", seeds = new[] { "https://fixture.test/" } });
        create.EnsureSuccessStatusCode();
        var runId = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("runId").GetGuid();

        const string markdown = "# Exact article\n\nA citation-safe paragraph.";
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
                        markdown,
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

        var markdownReadyAt = DateTimeOffset.UtcNow;
        using var complete = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new
            {
                status = "complete",
                completedAtUtc = markdownReadyAt,
                markdownReadyAt,
            });
        complete.EnsureSuccessStatusCode();
        var completeSnapshot = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            markdownReadyAt,
            completeSnapshot.GetProperty("markdownReadyAt").GetDateTimeOffset());

        Assert.Equal(markdown, Assert.Single(_factory.Repository.Pages(runId)).Markdown);
        Assert.Equal("https://fixture.test/contact", Assert.Single(_factory.Repository.Links(runId)).LinkUrl);
        await EventuallyAsync(() =>
            _factory.Rag.Requests.Any(r => r.Method == HttpMethod.Post && r.Path == "/v1/index"));

        using var resumed = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new { status = "external", clearMarkdownReadyAt = true });
        resumed.EnsureSuccessStatusCode();
        var resumedSnapshot = await resumed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, resumedSnapshot.GetProperty("markdownReadyAt").ValueKind);

        using var invalidReadiness = await owner.PatchAsJsonAsync(
            $"/api/geek-crawler/ingest/runs/{runId:D}",
            new
            {
                status = "complete",
                markdownReadyAt = DateTimeOffset.UtcNow,
                clearMarkdownReadyAt = true,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReadiness.StatusCode);
    }

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
                        url = "https://fixture.test/html-only",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = "<main>HTML only</main>",
                        markdown = (string?)null,
                    },
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/markdown-only",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = (string?)null,
                        markdown = "# Markdown only",
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
                    new
                    {
                        origin = "https://fixture.test",
                        url = "https://fixture.test/blank",
                        statusCode = 200,
                        robotsAllowed = true,
                        html = " ",
                        markdown = "\n",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("count").GetInt32());
        Assert.Equal(2, body.GetProperty("pages").GetArrayLength());
        Assert.Equal(3, body.GetProperty("rejectedCount").GetInt32());
        var reasons = body.GetProperty("rejectedReasonCounts");
        Assert.Equal(1, reasons.GetProperty("robotsDisallowed").GetInt32());
        Assert.Equal(1, reasons.GetProperty("failureReason").GetInt32());
        Assert.Equal(1, reasons.GetProperty("blankContent").GetInt32());
        Assert.Equal(
            body.GetProperty("rejectedCount").GetInt32(),
            reasons.EnumerateObject().Sum(reason => reason.Value.GetInt32()));

        var stored = _factory.Repository.Pages(runId);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, page => page.Url == "https://fixture.test/html-only" && page.Html is not null);
        Assert.Contains(stored, page => page.Url == "https://fixture.test/markdown-only" && page.Markdown is not null);
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
                        markdown = (string?)null,
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
            .RootElement.GetProperty("runId").GetGuid();

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

    private static async Task<Guid> CreateRunAsync(HttpClient owner)
    {
        using var create = await owner.PostAsJsonAsync(
            "/api/geek-crawler/ingest/runs",
            new { crawlType = "partner", seeds = new[] { "https://fixture.test/" } });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("runId").GetGuid();
    }

    private static async Task EventuallyAsync(Func<bool> assertion)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!assertion() && DateTime.UtcNow < timeout)
            await Task.Delay(20);
        Assert.True(assertion(), "Expected asynchronous protocol request was not observed.");
    }
}
