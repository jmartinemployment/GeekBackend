using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Partner grounding for the live "aiTool" content type, 2026-09-22. GenerateToolPageAsync never
/// consumed GccV2PartnerExtractionService's real payloads even though the "AI Tools" manual-entry
/// panel was deleted on the belief it already did (commit 7dcaea6). Per Jeff's explicit instruction
/// ("Partner crawl data required when content type Tool/Partner selected"), a create that reaches
/// this method must either ground in real partner extraction or refuse -- no silent ungrounded
/// fallback once a create is in play.
/// </summary>
public class GccGenerateServiceToolPageGroundingTests
{
    private const string ToolBodyJson =
        """{"sections":[{"tag":"h2","heading":"Overview","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}]}""";
    private const string ToolMetadataJson =
        """{"departmentListExcerpt":"x","summary":"x","mainSummary":"x","heroSummary":"x","homeSummary":"x","blogSummary":"x","toolPageExcerpt":"x","advertisingSummary":"x","metaDescription":"x"}""";

    private sealed class ScriptedProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            // Call 0 = tool body (sections array), call 1 = tool metadata (flat object).
            var content = Requests.Count == 1 ? ToolBodyJson : ToolMetadataJson;
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccCreateDto Create(string? researchJson) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "aiTool", Topic: "Partner Widget", Notes: "Operator-supplied brief text",
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: null, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private static GccGenerateService Build(
        IContentGenerationProvider provider, GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService partnerExtraction) => new(
        new ContentPromptBuilder(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag()),
        partnerExtraction);

    private static string ResearchJsonWithOnePartnerPage() =>
        GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    Url: "https://partner.test/widget",
                    Title: "Partner Widget",
                    Headings: [new HeadingDto(2, "Pricing")],
                    Paragraphs: ["Partner Widget starts at $19 per month, billed monthly."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

    [Fact]
    public async Task WithNoCreateContextTheLegacyUngroundedPathStillWorks()
    {
        // GenerateToolAsync (the legacy alias) and any other caller that never passes `create`
        // must keep working exactly as before -- this method's new fail-closed gate only applies
        // once a create is actually in play.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Some Tool", "A generic brief", "Some context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal("Some Tool", result.Name);
    }

    [Fact]
    public async Task ACreateWithNoPartnerResearchAtAllRefusesRatherThanGeneratingUngrounded()
    {
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Partner Widget", "brief", "context", "marketing", null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(researchJson: null)));

        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartnerPagesThatExtractToNothingUsableStillRefuses()
    {
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.Scripted(
            new FakeProviderFactory(provider), GccPartnerExtractionFakes.EmptyPageExtraction);
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Partner Widget", "brief", "context", "marketing", null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(ResearchJsonWithOnePartnerPage())));

        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealExtractionDataGroundsTheBodyPromptAndTheJsonLd()
    {
        var provider = new ScriptedProvider();
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()));

        // The body prompt's own request (call 0) actually carried the extraction, not just that
        // the call succeeded.
        var bodyRequest = provider.Requests[0];
        var userMessage = bodyRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("PERSISTED TOOL RESEARCH", userMessage, StringComparison.Ordinal);
        Assert.Contains("reduces setup time by half", userMessage, StringComparison.Ordinal);

        // Real partner JSON-LD, not the thin generic builder -- proven by an identity field the
        // generic SoftwareApplicationSchemaBuilder has no source for (the page's own title).
        Assert.Contains("\"name\":\"Partner Widget\"", result.JsonLdSchema);
    }

    [Fact]
    public async Task BriefNoLongerReachesTheModelFramedAsRevisionFeedback()
    {
        // The old positional call passed `brief` into BuildToolBodyPrompt's revisionNotes slot,
        // so a first-time generation read as "REVISION REQUIRED -- address the reviewer's
        // feedback: <brief text>". Confirmed gone -- brief still reaches the model, just correctly,
        // via app.Description ("Tool summary: ...").
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        await service.GenerateToolPageAsync(
            "Some Tool", "A generic brief", "Some context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var bodyRequest = provider.Requests[0];
        var system = bodyRequest.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.DoesNotContain("REVISION REQUIRED", system, StringComparison.Ordinal);
        var userMessage = bodyRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Tool summary:", userMessage, StringComparison.Ordinal);
    }
}
