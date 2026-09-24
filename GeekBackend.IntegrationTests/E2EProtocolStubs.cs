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
    private readonly ConcurrentDictionary<Guid, GccV2BriefDto> _gccBriefs = new();
    private readonly ConcurrentDictionary<Guid, GccV2JobDto> _gccJobs = new();
    private readonly ConcurrentDictionary<Guid, List<GccV2StageResultDto>> _gccStageResults = new();
    private readonly GccV2SkillPackageDto _skill = SkillFixture();
    private readonly IReadOnlyList<GccV2SkillAuditEventDto> _skillAudit = [];
    private readonly GccV2AgentDto _agent = AgentFixture();
    private readonly IReadOnlyList<GccV2AgentAuditEventDto> _agentAudit = [];
    private readonly ConcurrentDictionary<Guid, GccV2AgentTestRunDto> _agentTests = new();
    private readonly GccV2TaskAgentDefinitionDto _taskAgent = TaskAgentFixture();
    private readonly ConcurrentDictionary<Guid, GccV2TaskRunDto> _taskRuns = new();

    public IReadOnlyDictionary<Guid, GeekCrawlerRunDto> Runs => _runs;
    public IReadOnlyList<GeekCrawlerPageDto> Pages(Guid runId) =>
        _pages.TryGetValue(runId, out var pages) ? pages : [];
    public IReadOnlyList<GeekCrawlerLinkDto> Links(Guid runId) =>
        _links.TryGetValue(runId, out var links) ? links : [];
    public GccV2BriefDto? GccBrief(Guid id) => _gccBriefs.GetValueOrDefault(id);
    public IReadOnlyList<GccV2StageResultDto> GccStageResults(Guid jobId) =>
        _gccStageResults.TryGetValue(jobId, out var results) ? results : [];

    /// <summary>
    /// Seeds a job and its brief.
    ///
    /// <para>
    /// <paramref name="partnerSourceRunId"/> is what makes the brief citeable. The generation brief
    /// reads partner runs out of the brief JSON (<c>GccV2GenerationBriefAssembler.Assemble</c> ->
    /// <c>ReadRunIds</c>), and with none there the Create library writer queries no corpus at all:
    /// QueryRunsAsync returns empty on an empty run list, so the writer reaches its citation step
    /// with no pages and refuses the draft. A test that asserts a verified citation has to seed a
    /// run for that citation to come from. Left null by default so the retry-model tests, which
    /// never reach the writer, keep the brief they were written against.
    /// </para>
    /// </summary>
    public GccV2JobDto SeedFailedGccJob(
        Guid ownerUserId,
        string status = "failed",
        string stage = "write",
        Guid? partnerSourceRunId = null)
    {
        var createId = Guid.NewGuid();
        var briefJson = partnerSourceRunId is { } partnerRun
            ? $$"""
              {"modelPolicy":{"version":"content-model-policy.v1","preset":"best-quality"},
               "partnerSourceRunId":"{{partnerRun:D}}"}
              """
            : """{"modelPolicy":{"version":"content-model-policy.v1","preset":"best-quality"}}""";
        var brief = new GccV2BriefDto(
            Guid.NewGuid(), createId, 1, "retry model", "blog",
            briefJson,
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

        if (path == "repo/content-creator-v2/skills" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, new[] { _skill });
        if (path == $"repo/content-creator-v2/skills/{_skill.Id}" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, _skill);
        if (path == $"repo/content-creator-v2/skills/{_skill.Id}/audit" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, _skillAudit);
        var skillVersion = _skill.Versions[0];
        if (path == $"repo/content-creator-v2/skills/versions/{skillVersion.Id}/findings/{skillVersion.Findings[0].Id}"
            && request.Method == HttpMethod.Patch)
            return Json(HttpStatusCode.OK, skillVersion.Findings[0] with
            {
                Disposition = "resolved",
                ReviewerRationale = "Reviewed in integration test",
            });
        if (path == $"repo/content-creator-v2/skills/versions/{skillVersion.Id}/review"
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, skillVersion with { State = "approved" });
        if (path == $"repo/content-creator-v2/skills/versions/{skillVersion.Id}/publish"
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, skillVersion with { State = "published" });
        if (path == $"repo/content-creator-v2/skills/versions/{skillVersion.Id}/deprecate"
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, skillVersion with { State = "deprecated" });

        if (path == "repo/content-creator-v2/agents" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, new[] { _agent });
        if (path == $"repo/content-creator-v2/agents/{_agent.Id}" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, _agent);
        if (path == $"repo/content-creator-v2/agents/{_agent.Id}/audit" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, _agentAudit);
        var agentVersion = _agent.Versions[0];
        if (path == $"repo/content-creator-v2/agents/versions/{agentVersion.Id}/review"
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, agentVersion with { State = "approved" });
        if (path == $"repo/content-creator-v2/agents/versions/{agentVersion.Id}/test-runs"
            && request.Method == HttpMethod.Post)
        {
            var now = DateTimeOffset.UtcNow;
            var run = new GccV2AgentTestRunDto(
                Guid.NewGuid(), agentVersion.Id, agentVersion.VersionDigest, "contract", "{}",
                "queued", 0, "queued", null, null, GeekApiTestFactory.OwnerUserId.ToString("D"),
                now, null, null, now, null, null, null, 0, 0, null, null);
            _agentTests[run.Id] = run;
            return Json(HttpStatusCode.Accepted, run);
        }
        if (path == $"repo/content-creator-v2/agents/versions/{agentVersion.Id}/test-runs"
            && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, _agentTests.Values
                .Where(x => x.AgentVersionId == agentVersion.Id).OrderByDescending(x => x.QueuedAtUtc));
        if (path.StartsWith("repo/content-creator-v2/agents/test-runs/by-status/", StringComparison.Ordinal)
            && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, Array.Empty<GccV2AgentTestRunDto>());
        if (path.StartsWith("repo/content-creator-v2/agents/test-runs/", StringComparison.Ordinal)
            && Guid.TryParse(path.Split('/').Last(), out var testRunId)
            && request.Method == HttpMethod.Get
            && _agentTests.TryGetValue(testRunId, out var testRun))
            return Json(HttpStatusCode.OK, testRun);
        if (path.StartsWith("repo/content-creator-v2/agents/test-runs/", StringComparison.Ordinal)
            && request.Method == HttpMethod.Patch
            && Guid.TryParse(path.Split('/').Last(), out testRunId)
            && _agentTests.TryGetValue(testRunId, out testRun))
            return Json(HttpStatusCode.OK, testRun with
            {
                Phase = "cancellation-requested",
                CancellationRequestedAtUtc = DateTimeOffset.UtcNow,
            });
        if (path.StartsWith($"repo/content-creator-v2/agents/versions/{agentVersion.Id}/", StringComparison.Ordinal)
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, agentVersion);

        if (path == "repo/content-creator-v2/task-agents" && request.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, new[] { _taskAgent });
        if (path is "repo/content-creator-v2/task-agents/fact-density"
            || path == $"repo/content-creator-v2/task-agents/{_taskAgent.Id}")
            return Json(HttpStatusCode.OK, _taskAgent);
        if (path.StartsWith(
                $"repo/content-creator-v2/task-agents/versions/{_taskAgent.Versions[0].Id}/",
                StringComparison.Ordinal)
            && request.Method == HttpMethod.Post)
            return Json(HttpStatusCode.OK, _taskAgent.Versions[0]);
        if (path == "repo/content-creator-v2/task-runs" && request.Method == HttpMethod.Post)
        {
            var command = await request.Content!.ReadFromJsonAsync<CreateGccV2TaskRunCommand>(
                JsonOptions, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            var run = new GccV2TaskRunDto(
                id, command!.OwnerUserId, command.TaskAgentDefinitionId, command.TaskAgentVersionId,
                command.TaskAgentVersionDigest, command.InputJson, command.InputDigest,
                command.ContextManifestId, command.ContextManifestDigest,
                command.ModelSnapshotJson, command.ModelSnapshotDigest,
                command.BudgetSnapshotJson, command.BudgetSnapshotDigest,
                command.SourceSnapshotJson, command.SourceSnapshotDigest,
                "queued", "queued", 0, 0, 0, id, command.RetryOfRunId, command.Actor, command.Actor,
                null, null, null, null, null, now, now, null, null,
                [new(Guid.NewGuid(), id, 1, "queued", "{}", command.Actor, now)], []);
            _taskRuns[id] = run;
            return Json(HttpStatusCode.Created, run);
        }
        if (path.StartsWith("repo/content-creator-v2/task-runs/", StringComparison.Ordinal)
            && Guid.TryParse(path.Split('/')[3], out var taskRunId)
            && _taskRuns.TryGetValue(taskRunId, out var taskRun))
        {
            if (request.Method == HttpMethod.Get)
                return request.RequestUri?.Query.Contains(
                    $"ownerUserId={taskRun.OwnerUserId}", StringComparison.OrdinalIgnoreCase) is true
                    ? Json(HttpStatusCode.OK, taskRun)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && path.EndsWith("/transition", StringComparison.Ordinal))
            {
                var command = await request.Content!.ReadFromJsonAsync<TransitionGccV2TaskRunCommand>(
                    JsonOptions, cancellationToken);
                var updated = taskRun with
                {
                    Status = command!.Status,
                    Phase = command.Phase,
                    ProgressPercent = command.ProgressPercent,
                    CancellationRequestedAtUtc = command.CancellationRequested ? DateTimeOffset.UtcNow : null,
                    CancelledAtUtc = command.Status == "cancelled" ? DateTimeOffset.UtcNow : null,
                    CompletedAtUtc = command.Status == "cancelled" ? DateTimeOffset.UtcNow : null,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                _taskRuns[taskRunId] = updated;
                return Json(HttpStatusCode.OK, updated);
            }
        }

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

    private static GccV2SkillPackageDto SkillFixture()
    {
        var packageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var versionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var findingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var imported = new DateTimeOffset(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);
        var version = new GccV2SkillVersionDto(
            versionId, packageId, "1.2.3", new string('b', 40), new string('a', 64),
            new string('c', 64), "MIT", "gcc-v2", "published", imported, imported, imported,
            "reviewer", "approved", null,
            [new(Guid.NewGuid(), versionId, "SKILL.txt", "text/plain", 12, new string('d', 64), "Safe skill")],
            new[] { "researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete" }
                .Select(stage => new GccV2SkillApplicabilityDto(
                    Guid.NewGuid(), versionId, stage, "blog", 1, "[]",
                    """["load_evidence_page"]""", "automatic")).ToList(),
            [new(findingId, versionId, "medium", "gcc-static-v1", "external-tooling-request",
                "SKILL.md", 2, "Plugin request requires review.", "unreviewed", null, false)]);
        return new GccV2SkillPackageDto(
            packageId, "safe-writing", "Safe writing", "Ground prose in reviewed evidence.",
            "https://example.test/reviewed.git", "skills/safe-writing", "geek", "published",
            true, imported, null, [version]);
    }

    private static GccV2AgentDto AgentFixture()
    {
        var skillPackage = SkillFixture();
        var skill = skillPackage.Versions[0];
        var versionId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var version = new GccV2AgentVersionDto(
            versionId, Guid.Parse("88888888-8888-8888-8888-888888888888"), "1.0.0",
            "Produce canonical content.", """["blog"]""",
            """["load_evidence_page"]""", """["o3"]""", new string('e', 64),
            "published", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, null, "admin", "reviewed",
            """{"passed":true}""",
            [new(versionId, skill.Id, 0, new(
                skill.Id, skill.SemanticVersion, skill.PackageSha256, skill.State,
                new(skillPackage.Id, skillPackage.Slug, skillPackage.DisplayName),
                skill.Applicability))],
            new[] { "researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete" }
                .Select((stage, order) => new GccV2AgentStageParticipationDto(
                    Guid.NewGuid(), versionId, stage, "producer", 100 + order)).ToList(),
            "Produce evidence-grounded canonical content.", "content-model-policy.v1", []);
        return new(
            Guid.Parse("88888888-8888-8888-8888-888888888888"), "writing", "Writing",
            "Canonical content producer.", "published", true, DateTimeOffset.UtcNow, null, [version]);
    }

    private static GccV2TaskAgentDefinitionDto TaskAgentFixture()
    {
        var definitionId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var versionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var version = new GccV2TaskAgentVersionDto(
            versionId, definitionId, "1.0.0", "analysis",
            """{"contentType":["blog"],"marketingFunction":["seo"]}""",
            """{"additionalProperties":false,"properties":{"topic":{"type":"string"}},"required":["topic"],"type":"object"}""",
            new string('1', 64),
            """{"properties":{"score":{"type":"number"}},"type":"object"}""", new string('2', 64),
            """{"steps":[{"id":"analyze"}]}""", new string('3', 64),
            """{"manifest":"optional"}""", new string('4', 64),
            """{"component":"fact-density"}""", new string('5', 64),
            """{"downstream":["pillarArticle.v1"],"upstream":["entityMap.v1"]}""",
            """["search_corpus"]""", """["o3"]""", "[]",
            """{"minimumScore":0.8}""", new string('6', 64), new string('a', 64),
            "published", GeekApiTestFactory.OwnerUserId.ToString("D"),
            GeekApiTestFactory.OwnerUserId.ToString("D"), now, now, null, null);
        return new(
            definitionId, "fact-density", "Fact Density", "Measure claim and citation density.",
            "published", now, null, [version]);
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
        if (request.Method == HttpMethod.Get && path == "/v1/capabilities")
        {
            return Json(new
            {
                product = "geek-rag-library",
                features = new[] { "index", "query", "pages", "templates" },
                executionVersions = Array.Empty<string>(),
                skillEnvelopeVersions = Array.Empty<string>(),
                generationStages = Array.Empty<string>(),
                agentGenerationStages = Array.Empty<string>(),
                specialistExecutors = Array.Empty<string>(),
                specialistExecutorVersion = "",
                toolsAllowed = false,
                stageScopedToolsAllowed = false,
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
