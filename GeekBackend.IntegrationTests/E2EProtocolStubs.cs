extern alias GeekApi;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeekApi::GeekAPI.HttpClients;

namespace GeekBackend.IntegrationTests;

public sealed record CapturedRequest(
    HttpMethod Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

public sealed class InMemoryGeekRepositoryHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, GeekCrawlerRunDto> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<GeekCrawlerPageDto>> _pages = new();
    private readonly ConcurrentDictionary<Guid, List<GeekCrawlerLinkDto>> _links = new();

    public IReadOnlyDictionary<Guid, GeekCrawlerRunDto> Runs => _runs;
    public IReadOnlyList<GeekCrawlerPageDto> Pages(Guid runId) =>
        _pages.TryGetValue(runId, out var pages) ? pages : [];
    public IReadOnlyList<GeekCrawlerLinkDto> Links(Guid runId) =>
        _links.TryGetValue(runId, out var links) ? links : [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? "";

        if (request.Method == HttpMethod.Post && path == "repo/geek-crawler/runs")
        {
            var command = await request.Content!.ReadFromJsonAsync<CreateGeekCrawlerRunCommand>(
                JsonOptions,
                cancellationToken);
            var run = new GeekCrawlerRunDto(
                Guid.NewGuid(),
                command!.OwnerUserId,
                command.CrawlType,
                "pending",
                command.SeedUrlsJson ?? "[]",
                command.SeedKey,
                null,
                null,
                DateTimeOffset.UtcNow,
                null,
                null);
            _runs[run.Id] = run;
            return Json(HttpStatusCode.OK, run);
        }

        if (TryRunId(path, out var runId))
        {
            if (request.Method == HttpMethod.Get)
                return _runs.TryGetValue(runId, out var run)
                    ? Json(HttpStatusCode.OK, run)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.Method == HttpMethod.Patch && _runs.TryGetValue(runId, out var existing))
            {
                var patch = await request.Content!.ReadFromJsonAsync<PatchGeekCrawlerRunCommand>(
                    JsonOptions,
                    cancellationToken);
                var updated = existing with
                {
                    Status = patch!.Status ?? existing.Status,
                    HostProgressJson = patch.HostProgressJson ?? existing.HostProgressJson,
                    ErrorSummary = patch.ErrorSummary ?? existing.ErrorSummary,
                    StartedAtUtc = patch.StartedAtUtc ?? existing.StartedAtUtc,
                    CompletedAtUtc = patch.CompletedAtUtc ?? existing.CompletedAtUtc,
                    MarkdownReadyAt = patch.ClearMarkdownReadyAt
                        ? null
                        : patch.MarkdownReadyAt ?? existing.MarkdownReadyAt,
                };
                _runs[runId] = updated;
                return Json(HttpStatusCode.OK, updated);
            }
        }

        if (request.Method == HttpMethod.Post && path == "repo/geek-crawler/pages/batch")
        {
            var command = await request.Content!.ReadFromJsonAsync<CreateGeekCrawlerPageBatchCommand>(
                JsonOptions,
                cancellationToken);
            var pages = _pages.GetOrAdd(command!.RunId, _ => []);
            var created = command.Pages.Select(item =>
            {
                var page = new GeekCrawlerPageDto(
                    Guid.NewGuid(),
                    command.RunId,
                    item.Origin,
                    item.Url,
                    item.FinalUrl ?? item.Url,
                    item.StatusCode,
                    item.RobotsAllowed,
                    item.Html,
                    item.FailureReason,
                    DateTimeOffset.UtcNow,
                    item.Title,
                    item.Markdown,
                    item.Excerpt);
                lock (pages) pages.Add(page);
                return new GeekCrawlerCreatedPageDto(item.Url, page.Id);
            }).ToList();
            return Json(HttpStatusCode.OK, new GeekCrawlerPageBatchResult(created.Count, created));
        }

        if (request.Method == HttpMethod.Post && path == "repo/geek-crawler/links/batch")
        {
            var command = await request.Content!.ReadFromJsonAsync<CreateGeekCrawlerLinkBatchCommand>(
                JsonOptions,
                cancellationToken);
            var links = _links.GetOrAdd(command!.RunId, _ => []);
            lock (links)
            {
                links.AddRange(command.Links.Select(item => new GeekCrawlerLinkDto(
                    Guid.NewGuid(),
                    command.RunId,
                    item.PageId,
                    item.FromUrl,
                    item.LinkUrl,
                    item.IsSameOrigin,
                    DateTimeOffset.UtcNow)));
            }
            return Json(HttpStatusCode.OK, new { count = command.Links.Count });
        }

        // Workflow hydration and unrelated background services receive deterministic empty data.
        if (request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, Array.Empty<object>());

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static bool TryRunId(string path, out Guid runId)
    {
        const string prefix = "repo/geek-crawler/runs/";
        runId = default;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParse(path[prefix.Length..], out runId);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
        new(status) { Content = JsonContent.Create(value, options: JsonOptions) };
}

public sealed class RagProtocolStubHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    public const string ArticlePageId = "aaaaaaaaaaaaaaaaaaaaaaaa";
    public const string ArticleUrl = "https://fixture.test/article";
    public const string CitationQuote = "Deterministic citations must exactly match stored Markdown.";
    public const string ArticleMarkdown =
        "# Fixture article\n\nDeterministic citations must exactly match stored Markdown.\n\nMore text.";

    public bool FailRequests { get; set; }
    public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new CapturedRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? "",
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
            body));

        if (FailRequests)
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { error = "fixture failure" }),
            };

        var path = request.RequestUri?.AbsolutePath;
        if (request.Method == HttpMethod.Post && path == "/v1/index")
        {
            using var document = JsonDocument.Parse(body);
            var runId = document.RootElement.GetProperty("runId").GetString();
            return Json(new { runId, state = "queued", pagesSeen = 0, chunksUpserted = 0 });
        }

        if (request.Method == HttpMethod.Post && path == "/v1/query")
        {
            using var document = JsonDocument.Parse(body);
            var runId = document.RootElement.GetProperty("runId").GetString();
            return Json(new
            {
                runId,
                retrieval = "hybrid",
                chunks = new[]
                {
                    new
                    {
                        runId,
                        url = ArticleUrl,
                        finalUrl = ArticleUrl,
                        title = "Fixture article",
                        chunkIndex = 0,
                        text = CitationQuote,
                        pageId = ArticlePageId,
                        sectionTitle = "Fixture article",
                    },
                },
            });
        }

        if (request.Method == HttpMethod.Get && path == $"/v1/pages/{ArticlePageId}")
        {
            return Json(new
            {
                pageId = ArticlePageId,
                runId = Guid.Empty.ToString("D"),
                url = ArticleUrl,
                title = "Fixture article",
                markdown = ArticleMarkdown,
            });
        }

        if (request.Method == HttpMethod.Post && path == "/v1/generate")
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var stage = root.GetProperty("generationStage").GetString();
            var intent = root.GetProperty("writingIntent").GetString();
            var outline = stage == "outline"
                ? new[] { new { key = "section-1", heading = "Verified claims", brief = "Use evidence." } }
                : null;
            return Json(new
            {
                intent,
                content = stage == "section" ? "A section with a verified claim." : "A verified draft.",
                outline,
                citations = new[]
                {
                    new
                    {
                        pageId = ArticlePageId,
                        url = ArticleUrl,
                        title = "Fixture article",
                        quote = CitationQuote,
                    },
                },
                sources = new[] { new { pageId = ArticlePageId, url = ArticleUrl } },
                warnings = Array.Empty<string>(),
                retrieval = "hybrid",
                modelUsed = "fixture-model",
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: JsonOptions) };
}
