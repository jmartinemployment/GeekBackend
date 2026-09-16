using GeekAPI.Services.ContentCreatorV2.Write;
extern alias GeekApi;

using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using GeekApi::GeekAPI.HttpClients;
using GeekApi::GeekAPI.Services.GeekCrawler;
using GeekApi::GeekAPI.Services.ContentCreatorV2.Generation;
using GeekApi::GeekAPI.Services.ContentCreatorV2.Write;
using GeekApi::GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekApi::GeekAPI.Services.Rag;
using GeekApi::GeekAPI.Services.Workflow.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.IntegrationTests;

public sealed class RagClientContractTests : IClassFixture<GeekApiTestFactory>
{
    private readonly GeekApiTestFactory _factory;

    public RagClientContractTests(GeekApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Rag_status_exposes_shared_model_policy_contract()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync("/api/rag/status");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        Assert.Equal("content-model-policy.v1", root.GetProperty("modelPolicyVersion").GetString());
        Assert.Equal("o1-pro", root.GetProperty("approvedStageModels").GetProperty("outline")[0].GetString());
        Assert.Equal("o3", root.GetProperty("approvedStageModels").GetProperty("section")[0].GetString());
    }

    [Fact]
    public async Task Retry_model_is_job_scoped_audited_and_consumed()
    {
        var job = _factory.Repository.SeedFailedGccJob(GeekApiTestFactory.OwnerUserId);
        var sibling = _factory.Repository.SeedSiblingGccJob(job);
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/geek-content-creator-v2/jobs/{job.Id:D}/retry-model",
            new
            {
                stage = "section",
                model = "o1-pro",
                reason = "availability",
                confirmed = true,
                replacedAttemptId = "attempt-1",
            });
        response.EnsureSuccessStatusCode();

        var persistedBrief = _factory.Repository.GccBrief(job.BriefId);
        Assert.NotNull(persistedBrief);
        using (var unchanged = JsonDocument.Parse(persistedBrief!.RawBriefJson))
            Assert.Equal("best-quality", unchanged.RootElement.GetProperty("modelPolicy").GetProperty("preset").GetString());

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<GccV2JobModelPolicyOverrideStore>();
        var jobOverride = await store.LoadLatestAsync(job.Id, default);
        var siblingOverride = await store.LoadLatestAsync(sibling.Id, default);
        Assert.NotNull(jobOverride);
        Assert.Null(siblingOverride);
        Assert.Equal("attempt-1", jobOverride!.ReplacedAttemptId);

        var create = new GccV2CreateDto(
            job.CreateId, job.OwnerUserId, "Retry", "blog", DateTimeOffset.UtcNow, null);
        var brief = GccV2GenerationBriefAssembler.Assemble(job, persistedBrief, create, null);
        var policy = new ContentModelPolicy();
        Assert.Equal(
            "o1-pro",
            policy.Select(ContentGenerationStage.Section, brief, jobOverride).EffectiveModel);
        Assert.Equal(
            "o3",
            policy.Select(ContentGenerationStage.Section, brief, siblingOverride).EffectiveModel);
    }

    [Fact]
    public async Task Retry_model_rejects_running_job_without_persisting_override()
    {
        var job = _factory.Repository.SeedFailedGccJob(GeekApiTestFactory.OwnerUserId, "running");
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/geek-content-creator-v2/jobs/{job.Id:D}/retry-model",
            new { stage = "section", model = "o1-pro", reason = "availability", confirmed = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Repository.GccStageResults(job.Id));
    }

    [Fact]
    public async Task Retry_model_maps_final_synthesis_to_unambiguous_job_stage()
    {
        var job = _factory.Repository.SeedFailedGccJob(
            GeekApiTestFactory.OwnerUserId,
            stage: "final-synthesis");
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/geek-content-creator-v2/jobs/{job.Id:D}/retry-model",
            new
            {
                stage = "finalSynthesis",
                model = "o3",
                reason = "availability",
                confirmed = true,
            });
        response.EnsureSuccessStatusCode();
        var retried = await response.Content.ReadFromJsonAsync<GccV2JobDto>();
        Assert.Equal("final-synthesis", retried!.Stage);
        Assert.Equal("pending", retried.Status);

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<GccV2JobModelPolicyOverrideStore>();
        var jobOverride = await store.LoadLatestAsync(job.Id, default);
        Assert.Equal("o3", jobOverride!.StageModels["finalSynthesis"]);
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
        var page = await client.GetPageMarkdownAsync(RagProtocolStubHandler.ArticlePageId);

        Assert.Equal("queued", index?.State);
        Assert.Equal("hybrid", query?.Retrieval);
        var quoteable = Assert.Single(query!.Pages);
        Assert.Equal(RagProtocolStubHandler.ArticlePageId, quoteable.PageId);
        Assert.Equal(RagProtocolStubHandler.ArticleMarkdown, page?.Markdown);
        Assert.Contains(RagProtocolStubHandler.CitationQuote, page!.Markdown, StringComparison.Ordinal);

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

    [Fact]
    public async Task Validation_retry_model_is_job_scoped_and_selected()
    {
        var job = _factory.Repository.SeedFailedGccJob(
            GeekApiTestFactory.OwnerUserId,
            stage: "validate");
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/geek-content-creator-v2/jobs/{job.Id:D}/retry-model",
            new
            {
                stage = "validation",
                model = "o1-pro",
                reason = "availability",
                confirmed = true,
            });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<GccV2JobModelPolicyOverrideStore>();
        var jobOverride = await store.LoadLatestAsync(job.Id, default);
        var brief = GccV2GenerationBriefAssembler.Assemble(
            job,
            _factory.Repository.GccBrief(job.BriefId)!,
            null,
            null);
        var selection = new ContentModelPolicy().Select(
            ContentGenerationStage.Validation,
            brief,
            jobOverride);

        Assert.Equal("o1-pro", selection.EffectiveModel);
        Assert.Equal("o1-pro", jobOverride!.StageModels["validation"]);
    }

    [Fact]
    public async Task Generate_without_create_library_fails_closed()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GccV2CreateLibraryWriter>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(
                GeekApiTestFactory.OwnerUserId.ToString(),
                new CreateLibraryDraftRequest
                {
                    WritingIntent = RagWritingIntents.TechnicalArticle,
                    Topic = "Legacy generate path",
                    GenerationStage = "validation",
                    RequireCiteable = true,
                },
                default));

        Assert.Contains("CreateLibraryDraft", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_library_validation_stage_returns_local_validation()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GccV2CreateLibraryWriter>();
        const string draft = "# Draft\n\n## Introduction\n\nA source-grounded short form.";
        var partnerRunId = Guid.NewGuid();
        var result = await service.GenerateAsync(
            GeekApiTestFactory.OwnerUserId.ToString(),
            new CreateLibraryDraftRequest
            {
                WritingIntent = RagWritingIntents.TechnicalArticle,
                Topic = "Library validation",
                PartnerRunId = partnerRunId,
                GenerationStage = "validation",
                DraftContent = draft,
                CreateLibraryDraft = true,
                ExecutionVersion = RagProducerCapabilities.CreateLibraryExecutionVersion,
            },
            default);

        Assert.Equal(draft, result.Content);
        Assert.True(result.Validation!.Approved);
        Assert.Equal(RagProducerCapabilities.CreateLibraryExecutionVersion, result.Provenance?.ExecutionVersion);
        Assert.DoesNotContain(
            _factory.Rag.Requests,
            r => r.Path == "/v1/generate");
    }

    [Fact]
    public async Task Create_library_repair_requires_create_library_draft_flag()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GccV2CreateLibraryWriter>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(
                GeekApiTestFactory.OwnerUserId.ToString(),
                new CreateLibraryDraftRequest
                {
                    WritingIntent = RagWritingIntents.SocialAd,
                    Topic = "Repair grounded ad copy",
                    PartnerRunId = Guid.NewGuid(),
                    GenerationStage = "repair",
                    SectionHeading = "Advertising variation",
                    RequireCiteable = true,
                },
                default));
    }

    [Fact]
    public async Task Skill_snapshot_is_persisted_once_before_plan_and_reused()
    {
        var job = _factory.Repository.SeedFailedGccJob(GeekApiTestFactory.OwnerUserId);
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();

        var first = await GccV2SkillSnapshotStore.LoadOrCreateAsync(repo, job, default);
        var retry = await GccV2SkillSnapshotStore.LoadOrCreateAsync(repo, job, default);

        Assert.Equal(first.SnapshotHash, retry.SnapshotHash);
        Assert.Single(
            _factory.Repository.GccStageResults(job.Id),
            result => result.Stage == GccV2SkillSnapshotStore.StageName);
    }

    [Fact]
    public async Task Final_synthesis_stage_persists_verified_citations_and_provenance()
    {
        var job = _factory.Repository.SeedFailedGccJob(GeekApiTestFactory.OwnerUserId);
        var briefDto = _factory.Repository.GccBrief(job.BriefId)!;
        var generationBrief = GccV2GenerationBriefAssembler.Assemble(job, briefDto, null, null);
        var selection = new ContentModelPolicy().Select(
            ContentGenerationStage.FinalSynthesis,
            generationBrief);
        var provenance = new GccV2GenerationProvenance(
            generationBrief.Version,
            selection.PolicyVersion,
            "final-synthesis.v1",
            selection.RequestedModel,
            selection.EffectiveModel,
            "hybrid",
            [RagProtocolStubHandler.ArticlePageId],
            [],
            10,
            selection);
        var citation = new RagCitationDto
        {
            PageId = RagProtocolStubHandler.ArticlePageId,
            Url = RagProtocolStubHandler.ArticleUrl,
            Quote = RagProtocolStubHandler.CitationQuote,
        };
        var section = new GccV2WriteSection(
            "lede",
            "Introduction",
            "problem",
            new Section("h2", "Introduction", [new TextParagraph([new Run("Synthesized")])], null, []),
            false,
            [citation],
            provenance);
        var output = new GccV2WriteOutput
        {
            Title = "Synthesized",
            MetaDescription = "Meta",
            Lede = section,
            Sections = [],
            Citations = [citation],
            Provenance = [provenance],
        };
        var wc = new GccV2WriteContext(
            job,
            briefDto,
            null,
            new GccV2Outline([], []),
            null!,
            null!,
            generationBrief,
            null,
            GccV2SkillCatalog.Resolve(job.ContentType));

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GccV2WriteService>();
        await service.PersistFinalSynthesisAsync(
            wc,
            GeekApiTestFactory.OwnerUserId,
            output,
            default);

        var persisted = Assert.Single(
            _factory.Repository.GccStageResults(job.Id),
            result => result.Stage == "final-synthesis");
        using var json = JsonDocument.Parse(persisted.OutputJson);
        Assert.Equal(
            RagProtocolStubHandler.ArticlePageId,
            json.RootElement.GetProperty("citations")[0].GetProperty("pageId").GetString());
        Assert.Equal(
            "finalSynthesis",
            json.RootElement.GetProperty("provenance").GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Final_synthesis_returns_structured_output_for_validation_without_losing_tool_extras()
    {
        var job = _factory.Repository.SeedFailedGccJob(GeekApiTestFactory.OwnerUserId);
        var briefDto = _factory.Repository.GccBrief(job.BriefId)!;
        var generationBrief = GccV2GenerationBriefAssembler.Assemble(job, briefDto, null, null);
        var lede = new GccV2WriteSection(
            "lede",
            "Introduction",
            "problem",
            new Section("h2", "Introduction", [new TextParagraph([new Run("Before synthesis.")])], null, []),
            false,
            Sources:
            [
                new RagGenerateSourceDto
                {
                    PageId = RagProtocolStubHandler.ArticlePageId,
                    Url = RagProtocolStubHandler.ArticleUrl,
                    Kind = "page",
                },
            ]);
        var extras = new GccV2ToolPageWriteExtras(
            "partner",
            "fixture-tool",
            """{"@type":"SoftwareApplication"}""",
            ["rag"],
            "Excerpt",
            "Main",
            "Hero",
            "Home",
            "Blog",
            "Ad",
            "<p>Source</p>",
            "/pillar");
        var draft = new GccV2WriteOutput
        {
            Title = "Synthesis Input",
            MetaDescription = "Meta",
            Lede = lede,
            Sections = [],
            ToolPage = extras,
            Sources = lede.Sources!,
        };
        var wc = new GccV2WriteContext(
            job,
            briefDto,
            null,
            new GccV2Outline([], []),
            null!,
            null!,
            generationBrief,
            null,
            GccV2SkillCatalog.Resolve(job.ContentType));

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GccV2WriteService>();
        var synthesized = await service.FinalSynthesizeAsync(wc, draft, default);

        Assert.Equal("lede", synthesized.Lede.SectionKey);
        Assert.Equal("problem", synthesized.Lede.Job);
        Assert.Same(extras, synthesized.ToolPage);
        Assert.Equal("fixture-tool", synthesized.ToolPage!.Slug);
        Assert.Equal("finalSynthesis", synthesized.Provenance.Last().Stage);
        Assert.Single(synthesized.Citations);
        Assert.Contains(
            "Before synthesis.",
            synthesized.ToContentDocument().Lede.Paragraphs
                .OfType<TextParagraph>().SelectMany(p => p.Runs).Select(r => r.Text));
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
