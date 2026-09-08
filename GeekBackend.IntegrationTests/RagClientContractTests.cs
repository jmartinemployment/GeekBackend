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
            CanonicalBrief = JsonSerializer.SerializeToElement(new
            {
                version = "gcc-v2-generation-brief.v1",
                targetKeyword = "verified RAG",
            }),
            ModelPolicyPreset = "custom",
            ModelPolicyVersion = "content-model-policy.v1",
            StageModelOverrides = new Dictionary<string, string> { ["section"] = "o3" },
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
        Assert.Equal("o3", root.GetProperty("stageModelOverrides").GetProperty("section").GetString());
        Assert.Equal("content-model-policy.v1", root.GetProperty("modelPolicyVersion").GetString());
        Assert.Equal(
            "gcc-v2-generation-brief.v1",
            root.GetProperty("canonicalBrief").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("briefContext", out _));
        Assert.False(root.TryGetProperty("model", out _));
        Assert.Equal(RagProtocolStubHandler.ArticlePageId, section.Provenance!.EvidenceIds[0]);
        Assert.Equal(RagProtocolStubHandler.ArticlePageId, outline!.Outline![0].EvidenceIds[0]);
    }

    [Fact]
    public async Task Final_synthesis_forwards_full_contract_and_normalizes_provenance()
    {
        var client = _factory.Services.GetRequiredService<IGeekCrawlerRagClient>();
        const string draft = "# Draft\n\n## Introduction\n\nA complete draft.";
        var result = await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "technical-article",
            Topic = "Final synthesis",
            GenerationStage = "finalSynthesis",
            DraftContent = draft,
            Sources =
            [
                new GeekCrawlerRagGenerateSourceDto
                {
                    PageId = RagProtocolStubHandler.ArticlePageId,
                    Url = RagProtocolStubHandler.ArticleUrl,
                    Title = "Fixture article",
                    Entity = "Fixture Co",
                    CrawlType = "partner",
                    Kind = "page",
                },
            ],
            CanonicalBrief = JsonSerializer.SerializeToElement(new
            {
                version = "gcc-v2-generation-brief.v1",
                targetKeyword = "final synthesis",
            }),
            ModelPolicyPreset = "best-quality",
            ModelPolicyVersion = "content-model-policy.v1",
        });

        Assert.Equal(draft, result!.Content);
        Assert.Equal("finalSynthesis", result.Provenance!.GenerationStage);
        Assert.Equal("o1-pro", result.Provenance.ModelUsed);
        Assert.Single(result.Citations);

        var request = _factory.Rag.Requests.Last(r =>
            r.Path == "/v1/generate"
            && r.Body.Contains("\"generationStage\":\"finalSynthesis\"", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(draft, root.GetProperty("draftContent").GetString());
        Assert.Equal(
            "gcc-v2-generation-brief.v1",
            root.GetProperty("canonicalBrief").GetProperty("version").GetString());
        var source = Assert.Single(root.GetProperty("sources").EnumerateArray());
        Assert.Equal("Fixture Co", source.GetProperty("entity").GetString());
        Assert.Equal("partner", source.GetProperty("crawlType").GetString());
        Assert.Equal("page", source.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Validation_forwards_full_contract_and_parses_typed_result()
    {
        var client = _factory.Services.GetRequiredService<IGeekCrawlerRagClient>();
        const string draft = "# Draft\n\n## Introduction\n\nA source-grounded short form.";
        var result = await client.GenerateAsync(new GeekCrawlerRagGenerateRequest
        {
            WritingIntent = "Short Form",
            Topic = "Typed validation",
            GenerationStage = "validation",
            DraftContent = draft,
            Sources =
            [
                new GeekCrawlerRagGenerateSourceDto
                {
                    PageId = RagProtocolStubHandler.ArticlePageId,
                    Url = RagProtocolStubHandler.ArticleUrl,
                    CrawlType = "partner",
                    Kind = "page",
                },
            ],
            CanonicalBrief = JsonSerializer.SerializeToElement(new
            {
                version = "gcc-v2-generation-brief.v1",
                contentType = "social",
                targetKeyword = "typed validation",
            }),
            ModelPolicyPreset = "custom",
            ModelPolicyVersion = "content-model-policy.v1",
            StageModelOverrides = new Dictionary<string, string> { ["validation"] = "o3" },
        });

        Assert.NotNull(result);
        Assert.Null(result!.Content);
        Assert.True(result.Validation!.Approved);
        Assert.Equal(0, result.Validation.UnsupportedClaimCount);
        Assert.Equal(96, result.Validation.BriefAlignmentScore);
        Assert.Equal("validation", result.Provenance!.GenerationStage);
        Assert.Equal("o3", result.Provenance.ModelUsed);

        var request = _factory.Rag.Requests.Last(r =>
            r.Path == "/v1/generate"
            && r.Body.Contains("\"generationStage\":\"validation\"", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(draft, root.GetProperty("draftContent").GetString());
        Assert.Equal("social", root.GetProperty("canonicalBrief").GetProperty("contentType").GetString());
        Assert.Equal("partner", root.GetProperty("sources")[0].GetProperty("crawlType").GetString());
        Assert.Equal("custom", root.GetProperty("modelPolicyPreset").GetString());
        Assert.Equal("o3", root.GetProperty("stageModelOverrides").GetProperty("validation").GetString());
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
    public async Task Validation_missing_typed_scores_fails_closed()
    {
        _factory.Rag.MalformedValidation = true;
        try
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RagGenerateService>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GenerateAsync(
                    GeekApiTestFactory.OwnerUserId.ToString(),
                    new RagGenerateRequest
                    {
                        WritingIntent = RagWritingIntents.TechnicalArticle,
                        Topic = "Malformed validation",
                        GenerationStage = "validation",
                        DraftContent = "# Draft\n\n## Evidence\n\nA claim.",
                        Sources =
                        [
                            new RagGenerateSourceDto
                            {
                                PageId = RagProtocolStubHandler.ArticlePageId,
                                Url = RagProtocolStubHandler.ArticleUrl,
                                Kind = "page",
                            },
                        ],
                        CanonicalBrief = JsonSerializer.SerializeToElement(new
                        {
                            version = "gcc-v2-generation-brief.v1",
                            contentType = "blog",
                        }),
                        ModelPolicyPreset = "best-quality",
                        ModelPolicyVersion = ContentModelPolicy.CurrentVersion,
                        RequestedModel = ContentModelPolicy.O3,
                        RequireCiteable = true,
                    },
                    default));

            Assert.Contains("missing or malformed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _factory.Rag.MalformedValidation = false;
        }
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
            null);

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
            null);

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
