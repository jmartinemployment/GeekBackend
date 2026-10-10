extern alias GeekApi;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

        // Mirrors GeekCrawlerRunsController.GetForSlot -> MongoGeekCrawlerService.GetRunForSlotAsync:
        // newest run in the slot, "complete" only when publishedOnly, and 404 when the slot is
        // empty. 404 is the part that matters -- HttpGeekCrawlerRepository.GetAsync maps it to null,
        // which is how CreateRun learns the slot is free. Without this route the unmatched-GET
        // fallback below answered "[]", and an array cannot deserialize into a single
        // GeekCrawlerRunDto, so every ingest test died on a JsonException at the first byte.
        if (request.Method == HttpMethod.Get && path == "repo/geek-crawler/runs/for-slot")
        {
            var slot = ParseQuery(request.RequestUri);
            slot.TryGetValue("ownerUserId", out var slotOwnerUserId);
            slot.TryGetValue("crawlType", out var slotCrawlType);
            slot.TryGetValue("seedKey", out var slotSeedKey);
            var publishedOnly = slot.TryGetValue("publishedOnly", out var publishedOnlyRaw)
                && bool.TryParse(publishedOnlyRaw, out var publishedOnlyParsed)
                && publishedOnlyParsed;

            var slotRun = _runs.Values
                .Where(run => run.OwnerUserId == slotOwnerUserId
                              && run.CrawlType == slotCrawlType
                              && run.SeedKey == slotSeedKey
                              && (!publishedOnly || run.Status == "complete"))
                .OrderByDescending(run => run.CreatedAtUtc)
                .FirstOrDefault();

            return slotRun is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, slotRun);
        }

        // Storage headroom is not optional: CheckCapacityAsync refuses the crawl outright when it
        // reads null ("headroom could not be read ... No crawl was started"). AvgPageBytes null is
        // the honest fixture answer -- an empty corpus has no measured page size, which is the one
        // case the controller treats as nothing to check against.
        if (request.Method == HttpMethod.Get && path == "repo/geek-crawler/runs/storage-headroom")
            return Json(
                HttpStatusCode.OK,
                new GeekCrawlerStorageHeadroomDto(
                    TotalBytes: 100L * 1024 * 1024 * 1024,
                    FreeBytes: 80L * 1024 * 1024 * 1024,
                    AvgPageBytes: null,
                    AvgLinkBytes: null));

        // Both mirror their controllers: newest run matching the slot, else 404 -> null.
        if (request.Method == HttpMethod.Get
            && path is "repo/geek-crawler/runs/latest" or "repo/geek-crawler/runs/containing-seed")
        {
            var lookup = ParseQuery(request.RequestUri);
            lookup.TryGetValue("ownerUserId", out var lookupOwnerUserId);
            lookup.TryGetValue("crawlType", out var lookupCrawlType);
            // Both endpoints require a seed and filter on it: "latest" matches the whole seed set,
            // "containing-seed" matches one seed inside it. Ignoring the seed would hand back a run
            // from an unrelated slot -- and, since these tests share a class fixture, a run some
            // earlier test in the class left behind.
            var matchesSeed = path.EndsWith("containing-seed", StringComparison.Ordinal)
                ? new Func<GeekCrawlerRunDto, bool>(run =>
                    lookup.TryGetValue("seed", out var seed)
                    && run.SeedUrlsJson.Contains(seed, StringComparison.OrdinalIgnoreCase))
                : run => lookup.TryGetValue("seedsJson", out var seedsJson)
                    && string.Equals(run.SeedUrlsJson, seedsJson, StringComparison.Ordinal);

            var match = _runs.Values
                .Where(run => run.OwnerUserId == lookupOwnerUserId
                              && run.CrawlType == lookupCrawlType
                              && matchesSeed(run))
                .OrderByDescending(run => run.CreatedAtUtc)
                .FirstOrDefault();

            return match is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, match);
        }

        // Page activity has to report what this stub actually stored. The completion path refuses to
        // publish a run whose page count is zero ("no usable pages ... Nothing was published"), so a
        // 404 here would fail every ingest that had in fact stored pages. 404 is only correct for a
        // run this stub has never seen.
        if (request.Method == HttpMethod.Get && path == "repo/geek-crawler/pages/activity")
        {
            var activity = ParseQuery(request.RequestUri);
            if (!activity.TryGetValue("runId", out var activityRunIdRaw)
                || !Guid.TryParse(activityRunIdRaw, out var activityRunId)
                || !_pages.TryGetValue(activityRunId, out var storedPages))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return Json(
                HttpStatusCode.OK,
                new GeekCrawlerPageActivityDto(storedPages.Count, DateTimeOffset.UtcNow));
        }

        // Link activity only sizes a re-crawl estimate, and the controller falls back to its
        // first-crawl estimate when it is null.
        if (request.Method == HttpMethod.Get && path == "repo/geek-crawler/links/activity")
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        // PATCH repo/geek-crawler/runs/{id}/rag-index-status
        //
        // Absent until 2026-10-02, and invisible until GeekBackend 2123a1b. Before that the webhook
        // receiver wrapped its persist in catch { LogWarning } and returned Accepted regardless, so
        // the unmatched path fell through to the 404 fallback, the exception was swallowed, and the
        // contract test passed while the five Rag* fields were dropped on the floor. 2123a1b made a
        // failed persist return 404 -- which is what turned a silent gap into a failing test.
        //
        // Mirrors GeekCrawlerRunsController.PatchRagIndexStatus exactly: 204 when a document
        // matched, 404 when none did. 404 is the part under test; HttpGeekCrawlerRepository calls
        // EnsureSuccessStatusCode, and that is how a status written onto nothing reaches the sender.
        if (request.Method == HttpMethod.Patch && TryRagIndexStatusRunId(path, out var ragRunId))
        {
            if (!_runs.TryGetValue(ragRunId, out var target))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var patch = await request.Content!.ReadFromJsonAsync<PatchRagIndexStatusCommand>(
                JsonOptions,
                cancellationToken);
            _runs[ragRunId] = target with
            {
                RagState = patch!.RagState ?? target.RagState,
                RagChunksUpserted = patch.RagChunksUpserted ?? target.RagChunksUpserted,
                RagPagesEnglish = patch.RagPagesEnglish ?? target.RagPagesEnglish,
                RagPagesSkippedUnusable =
                    patch.RagPagesSkippedUnusable ?? target.RagPagesSkippedUnusable,
                RagIndexedAtUtc = patch.RagIndexedAtUtc ?? target.RagIndexedAtUtc,
            };
            return new HttpResponseMessage(HttpStatusCode.NoContent);
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
                    ContentReadyAt = patch.ClearContentReadyAt
                        ? null
                        : patch.ContentReadyAt ?? existing.ContentReadyAt,
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
                    item.Excerpt,
                    // contentHtml and blocks are the corpus body. Dropping them here made the stub
                    // store a page the crawler never sends and the Library cannot use -- the same
                    // shape whose loss cost 5,274 pages -- and left every assertion about stored
                    // content reading null.
                    item.ContentHtml,
                    item.Blocks);
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

    /// <summary>
    /// Query string as a case-insensitive map. The stub routes on AbsolutePath, so anything a
    /// handler needs from the query has to be read back out here.
    /// </summary>
    private static Dictionary<string, string> ParseQuery(Uri? uri)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = uri?.Query;
        if (string.IsNullOrEmpty(query))
            return values;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                values[Uri.UnescapeDataString(pair)] = "";
                continue;
            }

            values[Uri.UnescapeDataString(pair[..separator])] =
                Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return values;
    }

    private static bool TryRagIndexStatusRunId(string path, out Guid runId)
    {
        const string prefix = "repo/geek-crawler/runs/";
        const string suffix = "/rag-index-status";
        runId = default;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var middle = path[prefix.Length..^suffix.Length];
        return Guid.TryParse(middle, out runId);
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
    public const string CitationQuote = "Deterministic citations must exactly match the stored page text.";
    /// <summary>
    /// Mirrors Geek-Crawler-Rag's block->text projection: block text joined on blank lines, with no
    /// structural markers. A stub carrying heading syntax would let the contract test pass against a
    /// shape the Library never sends.
    /// </summary>
    public const string ArticleText =
        "Fixture article\n\nDeterministic citations must exactly match the stored page text.\n\nMore text.";

    /// <summary>
    /// The structural metadata Geek-Crawler-Rag returns on every hit (models.py ChunkHit): the
    /// parent block a matched child sits inside, the child span itself, and the anchors under the
    /// chunk's heading. Carried on the fixture because a stub that omits them lets the contract
    /// test pass against a payload the Library never sends -- which is exactly how anchors shipped
    /// typed as strings and took every query with a link down with it.
    /// </summary>
    /// <summary>
    /// The parent block, which must stay a verbatim span of <see cref="ArticleText"/>. Both are
    /// projections of the same page by the same block-text function upstream, so a quote the writer
    /// lifts out of the parent has to verify against the page text endpoint. A fixture that
    /// paraphrases the page here would fail quote verification for a reason no production payload
    /// can produce.
    /// </summary>
    public const string ChunkParentText = ArticleText;

    public const string ChunkChildText = CitationQuote;
    public const string ChunkRole = "child";

    /// <summary>
    /// Anchors are {label, href} objects, never strings. Two of them, so the test proves ordering
    /// and per-object binding rather than only that something arrived.
    /// </summary>
    public const string AnchorPricingLabel = "Pricing";
    public const string AnchorPricingHref = "https://fixture.test/pricing";
    public const string AnchorDocsLabel = "Docs";
    public const string AnchorDocsHref = "https://docs.fixture.test/start";

    /// <summary>
    /// The exact bytes the client receives from <c>/v1/query</c>. Built here and served here, so a
    /// test asserting against this payload is asserting against what production deserializes.
    /// </summary>
    public static string BuildQueryResponseJson(string? runId) =>
        JsonSerializer.Serialize(
            new
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
                        chunkRole = ChunkRole,
                        parentText = ChunkParentText,
                        childText = ChunkChildText,
                        anchors = new[]
                        {
                            new { label = AnchorPricingLabel, href = AnchorPricingHref },
                            new { label = AnchorDocsLabel, href = AnchorDocsHref },
                        },
                    },
                },
            },
            JsonOptions);

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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    BuildQueryResponseJson(runId),
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        if (request.Method == HttpMethod.Get && path == $"/v1/pages/{ArticlePageId}")
        {
            return Json(new
            {
                pageId = ArticlePageId,
                runId = Guid.Empty.ToString("D"),
                url = ArticleUrl,
                title = "Fixture article",
                text = ArticleText,
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: JsonOptions) };
}

/// <summary>
/// A deterministic stand-in for api.openai.com, so integration tests never depend on a key or
/// a network call.
///
/// <para>
/// This exists because of a two-week blind spot. <c>OpenAiProvider</c> falls back to the ambient
/// <c>OPENAI_API_KEY</c> environment variable when config carries none, so on a developer machine
/// the LLM tests quietly called the real API -- billing real tokens and passing -- while the same
/// tests failed in CI with "OpenAI API key is not configured". Every
/// "Cross-repository citation contract" run from 2026-09-11 onward was red for that reason, and
/// it looked green locally the whole time.
/// </para>
///
/// <para>
/// It answers by prompt kind because one writer makes several different calls, and each parses a
/// different shape. A synthesis request gets the draft echoed back verbatim: the draft already
/// arrives in the <c>{"title","sections"}</c> shape that prompt asks for, and the prompt demands
/// "Preserve factual claims" and "Preserve the section order and every heading EXACTLY" -- so
/// echoing is what a COMPLIANT model does. A stub that invented fresh prose would be simulating a
/// non-compliant one and would fail assertions that are about the pipeline carrying content
/// through, not about the model's wording.
/// </para>
/// </summary>
public sealed class OpenAiStubHandler : HttpMessageHandler
{
    /// <summary>Requests seen, for a test that wants to assert the LLM was reached at all.</summary>
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;

        var prompt = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var content = ChooseContent(prompt);

        var payload = JsonSerializer.Serialize(new
        {
            model = "stub-model",
            choices = new[]
            {
                new
                {
                    message = new { role = "assistant", content },
                    finish_reason = "stop",
                },
            },
            usage = new { prompt_tokens = 0, completion_tokens = 0 },
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// The assistant content to answer with, chosen from the prompt the writer composed.
    ///
    /// <para>
    /// The body is parsed as JSON rather than scanned as text. An earlier version did string
    /// surgery on the raw body and un-escaped it by hand, which silently left <c>\"</c> in the
    /// draft and produced content the writer rejected with "'\' is an invalid start of a property
    /// name". The parser already knows how to unescape; doing it twice by hand is how a fixture
    /// starts lying about its own shape.
    /// </para>
    /// </summary>
    private static string ChooseContent(string body)
    {
        var prompt = ReadPrompt(body);

        const string marker = "Draft to synthesize:";
        var index = prompt.IndexOf(marker, StringComparison.Ordinal);
        if (index >= 0)
        {
            var tail = prompt[(index + marker.Length)..].Trim();
            var open = tail.IndexOf('{');
            var close = tail.LastIndexOf('}');
            if (open >= 0 && close > open)
            {
                // Echo the draft verbatim. It already arrives in the {"title","sections"} shape
                // this prompt asks for, and the prompt demands the headings and claims survive --
                // so echoing is precisely what a compliant model returns.
                return tail[open..(close + 1)];
            }
        }

        if (prompt.Contains("\"outline\"", StringComparison.Ordinal))
        {
            return """{"outline":[{"key":"introduction","heading":"Introduction","brief":"Set up the problem","evidenceIds":[]}]}""";
        }

        return JsonSerializer.Serialize(new { title = "Synthesized", sections = Array.Empty<object>() });
    }

    /// <summary>Every message's content, joined — the prompt as the writer actually composed it.</summary>
    private static string ReadPrompt(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    parts.Add(content.GetString() ?? string.Empty);
                }
            }

            return string.Join("\n", parts);
        }
        catch (JsonException)
        {
            // A body this stub cannot read is not something to guess at.
            return string.Empty;
        }
    }
}
