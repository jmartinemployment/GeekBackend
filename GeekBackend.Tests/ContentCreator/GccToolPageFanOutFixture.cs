using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A <see cref="GccGenerateService"/> with a project that declares partner URLs, for the per-partner
/// tool-page fan-out.
/// </summary>
/// <remarks>
/// The fan-out reads partner URLs off the project, so the existing tool-page fixtures — which hand the
/// service a null project — cannot exercise it. This supplies one and nothing else; the model provider
/// is scripted to fail, because these tests are about which partners are attempted and how a refusal is
/// reported, not about what gets written.
/// </remarks>
internal sealed record GccToolPageFanOutFixture(
    GccGenerateService Service,
    GccCreateDto Create,
    GccToolPageFanOutFixture.CallCounter Calls)
{
    /// <summary>
    /// How many times extraction and drafting were each reached. The pre-flight's whole claim is that a
    /// partner it refuses is never drafted, and that a partner it passes is extracted once rather than
    /// twice — neither is observable from the outcomes, only from the call counts.
    /// </summary>
    internal sealed class CallCounter
    {
        private int _extractions;
        private int _drafts;

        private readonly List<string> _prompts = [];

        public int Extractions => Volatile.Read(ref _extractions);
        public int Drafts => Volatile.Read(ref _drafts);

        /// <summary>Every prompt the provider was handed, so a test can assert what the writer was
        /// actually told rather than inferring it from the output.</summary>
        public IReadOnlyList<string> Prompts
        {
            get { lock (_prompts) return [.. _prompts]; }
        }

        public void CountExtraction() => Interlocked.Increment(ref _extractions);

        public void CountDraft(string prompt)
        {
            Interlocked.Increment(ref _drafts);
            lock (_prompts) _prompts.Add(prompt);
        }
    }

    public static GccToolPageFanOutFixture WithPartners(params string[] partnerUrls) =>
        Build(GccPartnerExtractionFakes.EmptyPageExtraction, partnerUrls, pagesPerPartner: 0);

    /// <summary>
    /// A fan-out whose extraction returns <paramref name="extraction"/> for every page, with
    /// <paramref name="pagesPerPartner"/> retrieved pages per declared partner in the create's pooled
    /// research. Pages are what the slices bucket on, so a partner with none has nothing to extract from
    /// regardless of what the scripted extraction would have returned.
    /// </summary>
    /// <param name="draftable">
    /// Clears every gate drafting hits before the provider — <c>ValidateSiteSectionGate</c> (a project
    /// site run id) and <c>ValidateBriefRequired</c> (a complete brief). Without it, drafting refuses
    /// before it reaches extraction or the provider, which makes any assertion about extraction reuse or
    /// drafting cost **vacuously true**: a mutation that ignores the pre-flight entirely leaves the counts
    /// unchanged. Found exactly that way — the first version of these tests passed under that mutation.
    /// </param>
    /// <param name="briefJson">
    /// Replaces <c>CompleteBriefJson</c> when supplied. Must still satisfy <c>ValidateBriefRequired</c>
    /// if <paramref name="draftable"/> is set — a brief that fails validation refuses before drafting,
    /// which silently makes any assertion about prompt contents vacuous.
    /// </param>
    public static GccToolPageFanOutFixture Build(
        PartnerPageExtraction extraction,
        string[] partnerUrls,
        int pagesPerPartner,
        bool draftable = false,
        string? briefJson = null)
    {
        var projectId = Guid.NewGuid();
        var project = new GccProjectDto(
            Id: projectId,
            ClientId: Guid.NewGuid(),
            Name: "Acme",
            Code: null,
            Description: null,
            Status: "active",
            SiteUrl: "https://acme.test",
            ProjectSiteRunId: null,
            Department: "accounting",
            PartnerUrls: partnerUrls,
            CompetitorUrls: [],
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: null,
            FinishedDate: null,
            EstimatedHours: null,
            Budget: null,
            BudgetCurrency: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

        var projects = new GccCompetitorAnalysisResolverTests.FakeProjects(project);
        var calls = new CallCounter();
        var provider = new RefusingProvider(calls);

        var service = new GccGenerateService(
            new ContentPromptBuilder(),
            TestContentTypePrompts.Registry(),
            new FakeProviderFactory(provider),
            new SoftwareApplicationSchemaBuilder(),
            new BlogPostingSchemaBuilder(),
            new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
            Options.Create(new CompanyProfileOptions()),
            NullLogger<GccGenerateService>.Instance,
            GccCompetitorAnalysisResolverTests.Build(
                projects,
                new GccCompetitorAnalysisResolverTests.FakePages(),
                new GccCompetitorAnalysisResolverTests.FakeRag()),
            new GccV2PartnerExtractionService(
                new CountingSchemaConstrainedGenerator(extraction, calls),
                new FakeProviderFactory(provider),
                NullLogger<GccV2PartnerExtractionService>.Instance),
            projects,
            new GccPublisherProfileResolver(
                projects,
                new GccCompetitorAnalysisResolverTests.FakePages(),
                NullLogger<GccPublisherProfileResolver>.Instance),
            new GccKnownToolsResolver(
                new GccCompetitorAnalysisResolverTests.FakePages(),
                NullLogger<GccKnownToolsResolver>.Instance));

        var create = new GccCreateDto(
            Id: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            OwnerUserId: Guid.NewGuid(),
            StartingContentType: "tool",
            Topic: "Accounts Payable: Automated Data Entry & Processing",
            Notes: "notes",
            ProjectSiteRunId: draftable ? Guid.NewGuid() : null,
            SiteSectionJson: null,
            BriefJson: briefJson ?? (draftable ? CompleteBriefJson : null),
            ResearchJson: pagesPerPartner <= 0 ? null : ResearchFor(partnerUrls, pagesPerPartner),
            Status: "draft",
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            Department: "accounting",
            ProjectId: projectId);

        return new GccToolPageFanOutFixture(service, create, calls);
    }

    /// <summary>
    /// Every field <c>ValidateBriefRequired</c> demands, and nothing else. <c>angle</c> is
    /// <c>problem_solution</c> because that is the vocabulary the frontend sends and the one the tool
    /// page's angle-aware structure switches on.
    /// </summary>
    private const string CompleteBriefJson = """
        {
          "primaryIntent": "commercial",
          "buyingStage": "evaluation",
          "audienceSegment": "smb",
          "audienceNotes": "Accounts payable leads at 20-200 person firms.",
          "angle": "problem_solution",
          "ctaType": "demo",
          "toneOfVoice": "plain",
          "eeatSignals": ["practitioner experience"],
          "lengthBand": "long"
        }
        """;

    /// <summary>Pooled partner research: <paramref name="pagesPerPartner"/> pages under each declared
    /// host, which is what <see cref="GccPartnerToolSlices"/> buckets to build the slices.</summary>
    private static string ResearchFor(string[] partnerUrls, int pagesPerPartner)
    {
        var pages = new List<GccQuoteablePage>();
        foreach (var url in partnerUrls)
        {
            var host = new Uri(url).Host;
            for (var i = 0; i < pagesPerPartner; i++)
            {
                pages.Add(new GccQuoteablePage(
                    $"https://{host}/page-{i}",
                    $"{host} page {i}",
                    [new HeadingDto(2, "What it does")],
                    ["The product automates data entry for accounts payable teams."],
                    RunId: "run-1"));
            }
        }

        return GccResearchFetchService.Serialize(new GccResearchDocument(null, pages));
    }

    /// <summary>
    /// Records every prompt, answers the first call with a valid lede, then fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Failing the very first call records only the <b>lede</b> prompt, because the tool path writes the
    /// hook before the body (<c>GccGenerateService</c>: <i>"The hook is written before the body, so the
    /// body can continue it"</i>). Anything asserting on the <i>body</i> prompt therefore has to let the
    /// lede succeed first — otherwise the body prompt is never built and the assertion is vacuous.
    /// </para>
    /// <para>
    /// The shape is what <c>LlmResponseJsonParser.ParseLede</c> accepts: a <c>ledeType</c> from the
    /// strict taxonomy and at least one paragraph, which is the acceptance test rather than the heading.
    /// Everything after the lede still fails, so no page is ever written — these fixtures are about which
    /// prompts get built, not about output.
    /// </para>
    /// </remarks>
    private sealed class RefusingProvider(CallCounter calls) : IContentGenerationProvider
    {
        private const string ValidLedeJson = """
            {"ledeType":"summary","heading":"Opening",
             "paragraphs":[{"type":"text","runs":[{"text":"A scripted opening paragraph that is long enough to pass hygiene checks without saying anything of substance."}]}]}
            """;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var isFirst = calls.Drafts == 0;
            calls.CountDraft(string.Join("\n", request.Messages.Select(m => m.Content)));

            if (isFirst)
            {
                return Task.FromResult(new ChatCompletionResult(ValidLedeJson, "test-model", null, null));
            }

            throw new ContentGenerationException("scripted provider failure");
        }
    }

    /// <summary>
    /// <see cref="GccPartnerExtractionFakes.ScriptedSchemaConstrainedGenerator"/> with a call count —
    /// one call per page, per <c>GccV2PartnerExtractionService.ExtractOnePageAsync</c>.
    /// </summary>
    private sealed class CountingSchemaConstrainedGenerator(PartnerPageExtraction result, CallCounter calls)
        : IGccV2SchemaConstrainedGenerator
    {
        public Task<GccV2SchemaConstrainedCompletion<T>> CompleteAsync<T>(
            GccV2SchemaConstrainedRequest request, IContentGenerationProvider provider,
            System.Text.Json.JsonSerializerOptions? deserializeOptions, CancellationToken ct)
            where T : notnull
        {
            if (typeof(T) != typeof(PartnerPageExtraction))
                throw new NotSupportedException($"Fake only supports PartnerPageExtraction, got {typeof(T)}.");
            calls.CountExtraction();
            var completion = new GccV2SchemaConstrainedCompletion<PartnerPageExtraction>(
                result, "test-model", null, null);
            return Task.FromResult((GccV2SchemaConstrainedCompletion<T>)(object)completion);
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }
}
