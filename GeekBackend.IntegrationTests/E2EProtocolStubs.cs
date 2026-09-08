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
    private readonly ConcurrentDictionary<Guid, GccV2BriefDto> _gccBriefs = new();
    private readonly ConcurrentDictionary<Guid, GccV2JobDto> _gccJobs = new();
    private readonly ConcurrentDictionary<Guid, List<GccV2StageResultDto>> _gccStageResults = new();

    public IReadOnlyDictionary<Guid, GeekCrawlerRunDto> Runs => _runs;
    public IReadOnlyList<GeekCrawlerPageDto> Pages(Guid runId) =>
        _pages.TryGetValue(runId, out var pages) ? pages : [];
    public IReadOnlyList<GeekCrawlerLinkDto> Links(Guid runId) =>
        _links.TryGetValue(runId, out var links) ? links : [];
    public GccV2BriefDto? GccBrief(Guid id) => _gccBriefs.GetValueOrDefault(id);
    public IReadOnlyList<GccV2StageResultDto> GccStageResults(Guid jobId) =>
        _gccStageResults.TryGetValue(jobId, out var results) ? results : [];

    public GccV2JobDto SeedFailedGccJob(
        Guid ownerUserId,
        string status = "failed",
        string stage = "write")
    {
        var createId = Guid.NewGuid();
        var brief = new GccV2BriefDto(
            Guid.NewGuid(), createId, 1, "retry model", "blog",
            """{"modelPolicy":{"version":"content-model-policy.v1","preset":"best-quality"}}""",
            null, DateTimeOffset.UtcNow);
        var job = new GccV2JobDto(
            Guid.NewGuid(), "blog", brief.Id, ownerUserId.ToString("D"), createId,
            stage, status, 1, null, "model unavailable", null, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        _gccBriefs[brief.Id] = brief;
        _gccJobs[job.Id] = job;
        return job;
    }

    public GccV2JobDto SeedSiblingGccJob(GccV2JobDto sibling, string status = "failed")
    {
        var job = sibling with
        {
            Id = Guid.NewGuid(),
            Status = status,
            Error = "model unavailable",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _gccJobs[job.Id] = job;
        return job;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? "";

        if (TryGccResourceId(path, "jobs", out var gccJobId)
            && _gccJobs.TryGetValue(gccJobId, out var gccJob))
        {
            if (path.EndsWith("/stage-results", StringComparison.Ordinal))
            {
                var results = _gccStageResults.GetOrAdd(gccJobId, _ => []);
                if (request.Method == HttpMethod.Get)
                    return Json(HttpStatusCode.OK, results);
                if (request.Method == HttpMethod.Post)
                {
                    var command = await request.Content!.ReadFromJsonAsync<CreateGccV2StageResultCommand>(
                        JsonOptions, cancellationToken);
                    var result = new GccV2StageResultDto(
                        Guid.NewGuid(),
                        gccJobId,
                        command!.Stage,
                        command.SectionKey,
                        command.OutputJson ?? "{}",
                        command.TokensUsed ?? 0,
                        DateTimeOffset.UtcNow);
                    lock (results) results.Add(result);
                    return Json(HttpStatusCode.OK, result);
                }
            }
            if (request.Method == HttpMethod.Post
                && path.EndsWith("/events", StringComparison.Ordinal))
            {
                var command = await request.Content!.ReadFromJsonAsync<AppendGccV2JobEventCommand>(
                    JsonOptions, cancellationToken);
                return Json(
                    HttpStatusCode.OK,
                    new GccV2JobEventDto(
                        Guid.NewGuid(),
                        gccJobId,
                        1,
                        command!.Type,
                        command.PayloadJson ?? "{}",
                        DateTimeOffset.UtcNow));
            }
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, gccJob);
            if (request.Method == HttpMethod.Post && path.EndsWith("/transition", StringComparison.Ordinal))
            {
                var command = await request.Content!.ReadFromJsonAsync<ApplyGccV2JobTransitionCommand>(
                    JsonOptions, cancellationToken);
                var updated = gccJob with
                {
                    Stage = command!.Stage ?? gccJob.Stage,
                    Status = command.Status ?? gccJob.Status,
                    Error = command.Error ?? gccJob.Error,
                    ClaimedByInstanceId = command.ReleaseClaim is true ? null : gccJob.ClaimedByInstanceId,
                    ClaimedAtUtc = command.ReleaseClaim is true ? null : gccJob.ClaimedAtUtc,
                    LeaseUntilUtc = command.ReleaseClaim is true ? null : gccJob.LeaseUntilUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                _gccJobs[gccJobId] = updated;
                return Json(HttpStatusCode.OK, new GccV2JobTransitionResultDto(updated, null));
            }
        }

        if (TryGccResourceId(path, "briefs", out var gccBriefId)
            && _gccBriefs.TryGetValue(gccBriefId, out var gccBrief))
        {
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, gccBrief);
            if (request.Method == HttpMethod.Patch)
            {
                var command = await request.Content!.ReadFromJsonAsync<PatchGccV2BriefCommand>(
                    JsonOptions, cancellationToken);
                var updated = gccBrief with { RawBriefJson = command!.RawBriefJson ?? gccBrief.RawBriefJson };
                _gccBriefs[gccBriefId] = updated;
                return Json(HttpStatusCode.OK, updated);
            }
        }

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

    private static bool TryGccResourceId(string path, string resource, out Guid id)
    {
        id = Guid.Empty;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4
               && parts[0] == "repo"
               && parts[1] == "content-creator-v2"
               && parts[2] == resource
               && Guid.TryParse(parts[3], out id);
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
    public bool MalformedValidation { get; set; }
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
        if (request.Method == HttpMethod.Get && path == "/v1/capabilities")
        {
            return Json(new
            {
                executionVersions = new[] { "rag-generate.v2" },
                skillEnvelopeVersions = new[] { "gcc-skill-envelope.v1" },
                generationStages = new[] { "outline", "section", "repair", "validation", "finalSynthesis", "complete" },
                specialistExecutors = new[] { "researchPlanning", "outline", "section", "finalSynthesis", "validation", "repair" },
                specialistExecutorVersion = "bounded-specialists.v1",
                toolsAllowed = false,
            });
        }
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
            var preset = root.TryGetProperty("modelPolicyPreset", out var presetElement)
                ? presetElement.GetString()
                : null;
            var requestedModel = preset switch
            {
                "o3-only" => "o3",
                "best-quality" when stage is "outline" or "finalSynthesis" => "o1-pro",
                "best-quality" => "o3",
                "custom" when root.TryGetProperty("stageModelOverrides", out var overrides)
                              && overrides.TryGetProperty(stage!, out var model) => model.GetString(),
                _ => "fixture-model",
            };
            var outline = stage == "outline"
                ? new[] { new { key = "section-1", heading = "Verified claims", brief = "Use evidence.", evidenceIds = new[] { ArticlePageId } } }
                : null;
            object? validation = stage != "validation"
                ? null
                : MalformedValidation
                    ? new { approved = true, issues = Array.Empty<object>(), strengths = Array.Empty<string>() }
                    : new
                {
                    approved = true,
                    issues = Array.Empty<object>(),
                    strengths = new[] { "Grounded in the supplied evidence." },
                    unsupportedClaimCount = 0,
                    briefAlignmentScore = 96,
                    evidenceCoverageScore = 94,
                    usefulnessScore = 92,
                    originalityScore = 91,
                    brandAlignmentScore = 95,
                };
            return Json(new
            {
                intent,
                content = stage == "validation"
                    ? null
                    : stage == "section"
                    ? "A section with a verified claim."
                    : stage == "finalSynthesis" && root.TryGetProperty("draftContent", out var draft)
                        ? draft.GetString()
                        : "A verified draft.",
                outline,
                validation,
                citations = new[]
                {
                    new
                    {
                        pageId = ArticlePageId,
                        url = ArticleUrl,
                        title = "Fixture article",
                        sectionTitle = stage == "finalSynthesis" ? "Introduction" : null,
                        quote = CitationQuote,
                    },
                },
                sources = new[] { new { pageId = ArticlePageId, url = ArticleUrl } },
                warnings = Array.Empty<string>(),
                evidenceWarnings = Array.Empty<string>(),
                retrieval = "hybrid",
                modelUsed = requestedModel,
                provenance = new
                {
                    generationStage = stage,
                    modelUsed = requestedModel,
                    modelPolicyPreset = preset,
                    modelPolicyVersion = root.TryGetProperty("modelPolicyVersion", out var version)
                        ? version.GetString()
                        : null,
                    promptVersion = "citeable-generate.v2",
                    retrieval = "hybrid",
                    evidenceIds = new[] { ArticlePageId },
                    executionVersion = root.TryGetProperty("executionVersion", out var executionVersion)
                        ? executionVersion.GetString()
                        : "rag-generate.v1",
                    attemptId = root.TryGetProperty("attemptId", out var attemptId)
                        ? attemptId.GetString()
                        : Guid.NewGuid().ToString("D"),
                    skills = root.TryGetProperty("skillExecution", out var skills)
                        ? new
                        {
                            envelopeVersion = skills.GetProperty("envelopeVersion").GetString(),
                            catalogVersion = skills.GetProperty("catalogVersion").GetString(),
                            snapshotHash = skills.GetProperty("snapshotHash").GetString(),
                            stage,
                            skillVersions = skills.GetProperty("skills").EnumerateArray()
                                .Where(item => item.GetProperty("supportedStages").EnumerateArray()
                                    .Any(s => s.GetString() == stage))
                                .Select(item => $"{item.GetProperty("id").GetString()}@{item.GetProperty("version").GetString()}")
                                .ToArray(),
                        }
                        : null,
                },
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: JsonOptions) };
}
