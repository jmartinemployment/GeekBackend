using System.Text.Json;
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
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The retry is held to every check the draft was: a retry that fixes what it was asked to fix and
/// breaks something else is not what ships.
/// </summary>
/// <remarks>
/// <para>
/// One test per refusing check, through the real pillar path. The draft's only fault is an unlinked
/// closing -- a gap, so it would ship -- which earns it one retry. The retry links the closing and
/// carries exactly one violation of the check under test. Under the old per-guard retries the CTA
/// retry re-ran heading provenance and the partner mentions and nothing else, so a retry carrying a
/// quotation, a competitor link or an invented figure replaced a draft that had none.
/// </para>
/// <para>
/// A mutation of <c>GccDraftGuard</c> that drops a check from the retry -- or a call site that
/// guards only the draft -- turns the matching case here red.
/// </para>
/// </remarks>
public class GccGuardedRetryTests
{
    private const string Scheduler = "#consultationAppointment2xl";

    private const string LedeAndIntroJson =
        """{"lede":{"ledeType":"summary","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"An opening."}]}]},"introduction":{"tag":"h2","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"An introduction."}]}],"href":null,"children":[]}}""";

    private const string ArticleMetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string PaaSectionJson =
        """{"tag":"h2","heading":"People Also Ask","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Is it secure?","paragraphs":[{"type":"text","runs":[{"text":"It encrypts data at rest."}]}],"href":null,"children":[]}]}""";

    /// <summary>The paragraph each violation adds to a retry's sections, as section-JSON.</summary>
    public static TheoryData<string, string> Violations => new()
    {
        { "no-quotation", """{"type":"quote","runs":[{"text":"We love it."}],"cite":"https://partner.test/page"}""" },
        { "links", """{"type":"text","runs":[{"text":"Compare the rival.","href":"https://competitor.test/pricing"}]}""" },
        { "numbers", """{"type":"text","runs":[{"text":"Teams close the month 9137 hours sooner."}]}""" },
    };

    private sealed class ScriptedProvider(string? retryViolation, string? retryHeading = null) : IContentGenerationProvider
    {
        private int _bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public int BodyCalls => _bodyCalls;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;

            string content;
            if (request.JsonSchemaName == "sections")
            {
                var call = _bodyCalls++;
                var isRetry = asked.Contains("OMITTED ON THE LAST ATTEMPT", StringComparison.Ordinal);
                content = Batch(call, isRetry);
            }
            else if (system.Contains("""Write ONLY the "People Also Ask" FAQ section""", StringComparison.Ordinal))
            {
                content = PaaSectionJson;
            }
            else if (system.Contains("image-generation prompts", StringComparison.Ordinal))
            {
                content = ScriptedBody.ImagePrompts();
            }
            else if (system.Contains("sectionOutline", StringComparison.Ordinal))
            {
                content = ArticleMetadataJson;
            }
            else
            {
                content = LedeAndIntroJson;
            }

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        /// <summary>
        /// A draft batch closes without the scheduler link. A retry batch closes with it, plus the
        /// violation under test.
        /// </summary>
        private string Batch(int call, bool isRetry)
        {
            var letter = (char)('A' + (call % 26));
            var closing = isRetry
                ? $$"""{"type":"text","runs":[{"text":"Book a free consultation.","href":"{{Scheduler}}"}]}"""
                : """{"type":"text","runs":[{"text":"Book a free consultation."}]}""";
            var extra = isRetry && retryViolation is not null ? "," + retryViolation : string.Empty;
            var heading = isRetry && retryHeading is not null ? retryHeading : $"Planned section {letter}";
            return $$"""
                {"sections":[{"tag":"h2","heading":"{{heading}}","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{{closing}}{{extra}}],"href":null,"children":[],"provenance":"plan"}]}
                """;
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccCreateDto Create(string? briefJson = null) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "AI implementation", Notes: null,
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: null,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private static GccGenerateService Build(IContentGenerationProvider provider) => new(
        new ContentPromptBuilder(),
        TestContentTypePrompts.Registry(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        new BlogPostingSchemaBuilder(),
        new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag()),
        GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)),
        new GccCompetitorAnalysisResolverTests.FakeProjects(null),
        new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccPublisherProfileResolver>.Instance),
        new GccKnownToolsResolver(
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccKnownToolsResolver>.Instance),
        new GccToolPageFanOutFixture.FakeExtractionBank());

    private static (JsonElement Body, IReadOnlyList<string> Warnings) Read(string envelope)
    {
        using var doc = JsonDocument.Parse(envelope);
        var root = doc.RootElement;
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
        return (root.GetProperty("body").Clone(), warnings);
    }

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task A_retry_that_breaks_a_check_the_draft_passed_does_not_ship(string check, string violation)
    {
        var provider = new ScriptedProvider(violation);
        var service = Build(provider);

        var envelope = await service.GeneratePillarBodyAsync(
            Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        var (body, warnings) = Read(envelope);

        // The retry ran -- the draft's unlinked closing earned it -- and was not taken.
        var bodyText = body.GetRawText();
        Assert.DoesNotContain("We love it.", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("competitor.test", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("9137", bodyText, StringComparison.Ordinal);
        Assert.Contains(warnings, w => w.Contains("scheduler link", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(check));
    }

    [Fact]
    public async Task A_retry_that_adds_a_tools_section_does_not_ship()
    {
        var provider = new ScriptedProvider(retryViolation: null, retryHeading: "Best AP automation tools");
        var service = Build(provider);

        var envelope = await service.GeneratePillarBodyAsync(
            Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        var (body, _) = Read(envelope);

        Assert.DoesNotContain("Best AP automation tools", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_retry_replaces_a_draft_with_a_gap()
    {
        var provider = new ScriptedProvider(retryViolation: null);
        var service = Build(provider);

        var envelope = await service.GeneratePillarBodyAsync(
            Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        var (body, warnings) = Read(envelope);

        Assert.Contains(Scheduler, body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(warnings, w => w.Contains("scheduler link", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_people_also_ask_section_survives_a_retry()
    {
        // The mentions and CTA retries rebuilt the document from their own sections and dropped it.
        var provider = new ScriptedProvider(retryViolation: null);
        var service = Build(provider);
        const string brief = """{"paaQuestions":["Is it secure?"]}""";

        var envelope = await service.GeneratePillarBodyAsync(
            Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        var (body, _) = Read(envelope);

        var text = body.GetRawText();
        Assert.Contains(Scheduler, text, StringComparison.Ordinal);
        Assert.Contains("People Also Ask", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_prompt_failure_saves_the_guarded_draft_and_reports_it()
    {
        var provider = new FailingImagePromptsProvider(new ScriptedProvider(retryViolation: null));
        var service = Build(provider);

        var envelope = await service.GeneratePillarBodyAsync(
            Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        var (body, warnings) = Read(envelope);

        Assert.Contains(Scheduler, body.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(warnings, w => w.Contains("Image prompts were not written", StringComparison.Ordinal));
    }

    private sealed class FailingImagePromptsProvider(IContentGenerationProvider inner) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => inner.ProviderType;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
            return system.Contains("image-generation prompts", StringComparison.Ordinal)
                ? throw new ContentGenerationException("scripted image-prompt failure")
                : inner.CompleteAsync(request, cancellationToken);
        }
    }
}
