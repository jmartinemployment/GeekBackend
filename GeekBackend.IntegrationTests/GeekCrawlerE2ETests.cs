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

    private static async Task EventuallyAsync(Func<bool> assertion)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!assertion() && DateTime.UtcNow < timeout)
            await Task.Delay(20);
        Assert.True(assertion(), "Expected asynchronous protocol request was not observed.");
    }
}
