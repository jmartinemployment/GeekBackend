using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Can this partner answer the question the brief's Angle demands of the block quotation?
///
/// <para>
/// Validation asked a volume question — indexed, with enough pages and chunks — and volume is not
/// fitness. A partner can carry nine thousand chunks and answer nothing. These pin the three
/// outcomes apart, because reporting "the index was unreachable" as "this partner has nothing"
/// sends an operator to re-crawl a partner whose evidence is fine.
/// </para>
/// </summary>
public class GccAngleQuoteProbeTests
{
    private const string PartnerUrl = "https://stampli.com/case-studies/cti/";
    private static readonly Guid Run = Guid.NewGuid();

    /// <summary>The page as crawled — note the typographic apostrophe, as the live corpus writes it.</summary>
    private const string Published =
        "Before Stampli we chased paper for days. “We’ve cut approval time from nine days "
        + "to two, and nobody has looked back.” Invoices now route themselves.";

    private static GccAngleQuoteSpec Spec =>
        GccAngleQuoteQuestion.For("problem_solution", "Accounts Payable Automation")!;

    private static GccQuoteablePage Page(string paragraph) =>
        new(PartnerUrl, "CTI case study", [], [paragraph], PageId: "p1");

    private static GeekCrawlerRagQueryResult Retrieved(params GccQuoteablePage[] pages) =>
        new() { RunId = Run, Pages = pages };

    /// <summary>
    /// The crawl page behind a retrieved URL, carrying the same prose as a paragraph block.
    ///
    /// <para>
    /// The probe cuts its candidates from the typed blocks, exactly as the tool page does, so a test
    /// that supplied only retrieved page text would be exercising a source neither one reads. The
    /// text is the same string either way — what the blocks add is the block boundary, which is why
    /// a quotation stays whole instead of being sentence-split out of a flattened projection.
    /// </para>
    /// </summary>
    private static GeekCrawlerPageDto Crawled(string paragraph) => new(
        Id: Guid.NewGuid(),
        RunId: Run,
        Origin: PartnerUrl,
        Url: PartnerUrl,
        FinalUrl: PartnerUrl,
        StatusCode: 200,
        RobotsAllowed: true,
        Html: null,
        FailureReason: null,
        CrawledAtUtc: DateTimeOffset.UtcNow,
        Title: "CTI case study",
        Excerpt: null,
        ContentHtml: null,
        Blocks: JsonSerializer.SerializeToElement(new[]
        {
            new Dictionary<string, string> { ["kind"] = "paragraph", ["text"] = paragraph },
        }));

    private static GccAngleQuoteProbe Build(
        GeekCrawlerRagQueryResult? retrieved,
        GccAngleQuoteSelection? selection,
        bool providerAvailable = true,
        Exception? selectorThrows = null,
        bool blocksReadable = true) =>
        new(new FakeRag(retrieved),
            new GccTypedPassageReader(new FakePages(
                blocksReadable && retrieved?.Pages is { Count: > 0 }
                    ? retrieved.Pages.Select(page => Crawled(page.Paragraphs[0])).ToList()
                    : [])),
            new FakeGenerator(selection, selectorThrows),
            new FakeProviders(providerAvailable),
            NullLogger<GccAngleQuoteProbe>.Instance);

    private sealed class FakePages(IReadOnlyList<GeekCrawlerPageDto> pages) : IGccCrawlPageReader
    {
        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default) =>
            Task.FromResult(pages);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    [Fact]
    public async Task A_verbatim_span_answering_the_angle_is_accepted_and_cited_to_its_page()
    {
        var probe = Build(
            Retrieved(Page(Published)),
            new GccAngleQuoteSelection(true, 1, "names the manual pain and the fix"));

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.True(finding.CanAnswer);
        Assert.Equal(PartnerUrl, finding.CiteUrl);
        // The page's own characters, read back from the candidate list by number — the model
        // returned 1 and nothing else, so there is nothing that could have been altered.
        Assert.Contains("nine days to two", finding.QuoteText!, StringComparison.Ordinal);
        Assert.Contains('’', finding.QuoteText!);
    }



    [Fact]
    public async Task An_explicit_no_is_a_correct_answer_and_carries_the_reason()
    {
        // Without this the check is worthless: a model told to choose a quotation always chooses
        // one, so every partner would validate regardless of what the evidence says.
        var probe = Build(
            Retrieved(Page(
                "We are a payments company founded in 2015 and headquartered in Mountain View.")),
            new GccAngleQuoteSelection(false, null, "nothing here names a manual-process pain"));

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.NoAnswer, finding.Outcome);
        Assert.Contains("manual-process pain", finding.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_quote_naming_no_candidate_is_refused()
    {
        var probe = Build(
            Retrieved(Page(Published)),
            new GccAngleQuoteSelection(true, null, "fits"));

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.NoAnswer, finding.Outcome);
    }

    [Fact]
    public async Task A_candidate_id_outside_the_offered_range_is_refused()
    {
        var probe = Build(
            Retrieved(Page(Published)),
            new GccAngleQuoteSelection(true, 7, "fits"));

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.NoAnswer, finding.Outcome);
    }

    [Fact]
    public async Task An_unreachable_index_is_unavailable_not_a_verdict_on_the_partner()
    {
        var probe = Build(new GeekCrawlerRagQueryResult { RunId = Run, Pages = [], Failed = true }, null);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.Unavailable, finding.Outcome);
        Assert.False(finding.CanAnswer);
    }

    [Fact]
    public async Task Pages_that_cannot_be_read_back_are_unavailable_not_a_verdict()
    {
        // Retrieval answered with pages; the crawl store could not produce their blocks. That is the
        // store being unreadable, not this partner having nothing to say -- and the two must not
        // read alike, or an operator re-crawls a partner whose evidence is fine.
        var probe = Build(Retrieved(Page(Published)), null, blocksReadable: false);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.Unavailable, finding.Outcome);
        Assert.Contains("could not be read back", finding.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_quotable_in_what_was_retrieved_is_a_verdict_on_the_partner()
    {
        // Retrieval answered; none of it is shaped like a quotation. That is this partner's answer.
        var probe = Build(Retrieved(Page("pricing | plans | enterprise")), null);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.NoAnswer, finding.Outcome);
    }

    [Fact]
    public async Task Retrieval_returning_nothing_is_a_verdict_on_the_partner()
    {
        // The index answered; it simply has nothing matching. That is this partner's answer, and it
        // reads differently from the index being down.
        var probe = Build(Retrieved(), null);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.NoAnswer, finding.Outcome);
    }

    [Fact]
    public async Task No_provider_is_unavailable_not_a_verdict()
    {
        var probe = Build(Retrieved(Page(Published)), null, providerAvailable: false);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.Unavailable, finding.Outcome);
    }

    [Fact]
    public async Task A_selector_that_throws_is_unavailable_not_a_verdict()
    {
        var probe = Build(
            Retrieved(Page(Published)), null, selectorThrows: new HttpRequestException("502"));

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.Unavailable, finding.Outcome);
    }

    [Fact]
    public async Task No_run_id_is_unavailable_and_nothing_is_retrieved()
    {
        var probe = Build(Retrieved(Page(Published)), null);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Guid.Empty, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi, CancellationToken.None);

        Assert.Equal(GccAngleQuoteOutcome.Unavailable, finding.Outcome);
    }

    [Theory]
    [InlineData("problem_solution")]
    [InlineData("comparative")]
    [InlineData("case_study_data")]
    [InlineData("ultimate_guide")]
    public void Every_angle_in_the_vocabulary_has_a_question(string angle)
    {
        var spec = GccAngleQuoteQuestion.For(angle, "Accounts Payable Automation");

        Assert.NotNull(spec);
        Assert.Contains("Accounts Payable Automation", spec!.Need, StringComparison.Ordinal);
        Assert.NotEmpty(spec.Rule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("listicle")]
    public void An_angle_outside_the_vocabulary_has_no_question(string? angle)
    {
        // Defaulting would validate a partner against a question the brief never asked.
        Assert.Null(GccAngleQuoteQuestion.For(angle, "Accounts Payable Automation"));
    }

    [Fact]
    public void No_topic_means_no_question()
    {
        Assert.Null(GccAngleQuoteQuestion.For("problem_solution", "   "));
    }

    private sealed class FakeGenerator(GccAngleQuoteSelection? selection, Exception? throws)
        : IGccSchemaConstrainedGenerator
    {
        public Task<GccSchemaConstrainedCompletion<T>> CompleteAsync<T>(
            GccSchemaConstrainedRequest request,
            IContentGenerationProvider provider,
            JsonSerializerOptions? deserializeOptions,
            CancellationToken ct) where T : notnull
        {
            if (throws is not null) throw throws;
            return Task.FromResult(
                new GccSchemaConstrainedCompletion<T>((T)(object)selection!, "fake", null, null));
        }
    }

    [Fact]
    public async Task The_probe_asks_the_provider_the_operator_chose_and_never_the_default()
    {
        var providers = new RecordingProviders();
        var retrieved = Retrieved(Page(Published));
        var probe = new GccAngleQuoteProbe(
            new FakeRag(retrieved),
            new GccTypedPassageReader(new FakePages([Crawled(Published)])),
            new FakeGenerator(new GccAngleQuoteSelection(true, 1, "fits"), null),
            providers,
            NullLogger<GccAngleQuoteProbe>.Instance);

        await probe.ProbeAsync(
            Spec, PartnerUrl, Run, GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.Anthropic, CancellationToken.None);

        Assert.Equal([GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.Anthropic], providers.Requested);
    }

    [Fact]
    public async Task No_provider_sent_is_the_configured_default_not_a_refusal()
    {
        // The workspace does not send one yet. Absent is LlmProviders:DefaultProvider -- what the probe
        // always used -- resolved through GetDefault(), which refuses a misconfigured value itself.
        var providers = new DefaultOnlyProviders();
        var probe = new GccAngleQuoteProbe(
            new FakeRag(Retrieved(Page(Published))),
            new GccTypedPassageReader(new FakePages([Crawled(Published)])),
            new FakeGenerator(new GccAngleQuoteSelection(true, 1, "fits"), null),
            providers,
            NullLogger<GccAngleQuoteProbe>.Instance);

        var finding = await probe.ProbeAsync(Spec, PartnerUrl, Run, providerType: null, CancellationToken.None);

        Assert.True(finding.CanAnswer);
        Assert.Equal(1, providers.DefaultCalls);
    }

    private sealed class DefaultOnlyProviders : IContentProviderFactory
    {
        public int DefaultCalls { get; private set; }

        public IContentGenerationProvider Get(LlmProviderType providerType) =>
            throw new InvalidOperationException("Get() was called with no provider chosen.");

        public IContentGenerationProvider GetDefault()
        {
            DefaultCalls++;
            return new FakeProvider();
        }
    }

    /// <summary>Records which provider was asked for; the default is a failure, not an answer.</summary>
    internal sealed class RecordingProviders : IContentProviderFactory
    {
        public List<LlmProviderType> Requested { get; } = [];

        public IContentGenerationProvider Get(LlmProviderType providerType)
        {
            Requested.Add(providerType);
            return new FakeProvider();
        }

        public IContentGenerationProvider GetDefault() =>
            throw new InvalidOperationException("GetDefault() was called: the operator's provider was ignored.");
    }

    private sealed class FakeProviders(bool available) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => GetDefault();

        public IContentGenerationProvider GetDefault() =>
            available
                ? new FakeProvider()
                : throw new InvalidOperationException("no provider configured");
    }

    /// <summary>Never called — the generator is faked, so nothing reaches a provider.</summary>
    private sealed class FakeProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => default;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRag(GeekCrawlerRagQueryResult? result) : IGeekCrawlerRagClient
    {
        public bool IsEnabled => true;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need, Guid runId, string? crawlType = null, string? host = null, int topK = 8,
            bool? preferParent = null, bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null, string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) =>
            Task.FromResult(result);

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateIndexResult?>(null);

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need, int topK = 5, string? channel = null,
            IReadOnlyList<string>? entityTags = null, CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateQueryResult?>(null);

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId, CancellationToken ct = default, string? runId = null) =>
            Task.FromResult<GeekCrawlerRagPageText?>(null);
    }
}
