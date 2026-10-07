using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
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
/// A draft is written once, judged once, and either refused or shipped with its gaps reported. Nothing
/// is sent to the model a second time (Jeff, 2026-10-06: "NO RETRIES ... So I paid twice for nothing").
/// </summary>
/// <remarks>
/// Through the real pillar path: the provider counts the body calls it is asked for, so a second pass
/// over any batch is a failing count rather than a quiet extra charge.
/// </remarks>
public class GccDraftIsGuardedOnceTests
{
    private const string Scheduler = "#consultationAppointment2xl";

    private const string LedeAndIntroJson =
        """{"lede":{"ledeType":"summary","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"An opening."}]}]},"introduction":{"tag":"h2","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"An introduction."}]}],"href":null,"children":[]}}""";

    private const string ArticleMetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string PaaSectionJson =
        """{"tag":"h2","heading":"People Also Ask","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Is it secure?","paragraphs":[{"type":"text","runs":[{"text":"It encrypts data at rest."}]}],"href":null,"children":[]}]}""";

    /// <summary>
    /// Writes each body batch once. The closing is not linked to the scheduler, which is a gap and not a
    /// refusal, so the draft ships with it reported. <paramref name="heading"/> replaces the section
    /// headings, to put a refusing fault in the draft.
    /// </summary>
    private sealed class ScriptedProvider(string? heading = null, bool keyword = false) : IContentGenerationProvider
    {
        private int _bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public int BodyCalls => _bodyCalls;

        private readonly List<string> _prompts = [];

        /// <summary>Everything the writer was sent, every message of every call.</summary>
        public string AllPrompts
        {
            get { lock (_prompts) return string.Join("\n", _prompts); }
        }

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            lock (_prompts) _prompts.Add(string.Join("\n", request.Messages.Select(m => m.Content)));
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;

            string content;
            if (request.JsonSchemaName == "sections")
            {
                content = Batch(_bodyCalls++);
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

        private string Batch(int call)
        {
            var letter = (char)('A' + (call % 26));
            var title = heading ?? (keyword
                ? $"What AI implementation changes in part {letter}"
                : $"Planned section {letter}");
            return $$"""
                {"sections":[{"tag":"h2","heading":"{{title}}","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation."}]}],"href":null,"children":[],"provenance":"plan"}]}
                """;
        }
    }

    /// <summary>Answers a revision with two sections of the same weight as the draft being revised.</summary>
    private sealed class ReviseProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string Sent { get; private set; } = string.Empty;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Sent += string.Join("\n", request.Messages.Select(m => m.Content)) + "\n";
            const string content = """
                {"sections":[
                {"tag":"h2","heading":"Where the hours go","paragraphs":[{"type":"text","runs":[{"text":"Invoices are keyed twice, revised."}]}],"href":null,"children":[]},
                {"tag":"h2","heading":"People Also Ask","paragraphs":[{"type":"text","runs":[{"text":"It encrypts data at rest."}]}],"href":null,"children":[]}]}
                """;
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
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

    private static async Task<(JsonElement Body, IReadOnlyList<string> Warnings)> Pillar(
        IContentGenerationProvider provider, string? briefJson = null)
    {
        var envelope = await Build(provider).GeneratePillarBodyAsync(
            Create(briefJson), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
        return Read(envelope);
    }

    [Fact]
    public async Task A_draft_whose_writer_wrote_no_booking_line_ends_on_the_pages_own_and_is_not_written_again()
    {
        var provider = new ScriptedProvider();

        var (body, warnings) = await Pillar(provider);

        // The writer never linked the scheduler, and the page does: the closing is built by code, so the
        // gap this test used to report cannot occur, and nothing was asked a second time.
        Assert.DoesNotContain(warnings, w => w.Contains("scheduler link", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("retry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Scheduler, body.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Book your free consultation", body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(3, provider.BodyCalls);
    }

    private const string BriefWithQuestions = """
        {"paaQuestions":["Is it secure?"],"ctaType":"book_now","ctaLabel":"Book a consult",
         "nicheFraming":{"diagnosisQuestions":"What business objective should this automation serve?\nHow clean is the data the approvals draw on today?"}}
        """;

    [Fact]
    public async Task The_pillar_ends_its_last_body_section_on_the_pages_closing_before_the_people_also_ask_section()
    {
        var provider = new ScriptedProvider();

        var (body, _) = await Pillar(provider, BriefWithQuestions);

        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal("People Also Ask", sections[^1].GetProperty("heading").GetString());
        var paragraphs = sections[^2].GetProperty("paragraphs").EnumerateArray().ToList();
        var list = paragraphs[^1];
        var line = paragraphs[^2];
        Assert.Equal("text", line.GetProperty("type").GetString());
        var runs = line.GetProperty("runs").EnumerateArray().ToList();
        Assert.Equal(["Answer these questions when ", "booking your free consultation", "."], runs.Select(r => r.GetProperty("text").GetString()));
        Assert.Equal(Scheduler, runs[1].GetProperty("href").GetString());
        Assert.Equal("list", list.GetProperty("type").GetString());
        Assert.Equal(
            ["What business objective should this automation serve?", "How clean is the data the approvals draw on today?"],
            list.GetProperty("items").EnumerateArray().Select(item => item.EnumerateArray().Single().GetProperty("text").GetString()));
        // Once: no other section carries the line.
        Assert.Equal(1, sections.Sum(s => s.GetProperty("paragraphs").EnumerateArray().Count(p =>
            p.GetProperty("type").GetString() == "text"
            && p.GetProperty("runs").EnumerateArray().Any(r => r.TryGetProperty("href", out var h) && h.GetString() == Scheduler))));
    }

    [Fact]
    public async Task The_operators_questions_and_the_cta_setting_never_reach_the_writer()
    {
        var provider = new ScriptedProvider();

        await Pillar(provider, BriefWithQuestions);

        var prompts = provider.AllPrompts;
        string[] never =
        [
            "What business objective should this automation serve?", "How clean is the data", "book_now", "Book a consult",
            Scheduler, "CLOSING:",
        ];
        var found = never.Where(needle => prompts.Contains(needle, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 0, "The writer was sent: " + string.Join(" | ", found)
            + " -- " + string.Join(" || ", found.Select(f => prompts[Math.Max(0, prompts.IndexOf(f, StringComparison.Ordinal) - 80)..Math.Min(prompts.Length, prompts.IndexOf(f, StringComparison.Ordinal) + 120)])));
        Assert.Contains("END OF THE PAGE", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revision_never_shows_the_writer_the_closing_and_the_page_keeps_it_once_where_it_was()
    {
        var company = new CompanyProfileOptions();
        var closing = GccClosing.Paragraphs(company, ["What business objective should this automation serve?"]);
        Section Text(string heading, string text) =>
            new("h2", heading, [new TextParagraph([new Run(text)])], null, [], Provenance: "plan");
        var stored = new ContentDocument(
            Text("Opening", "An opening."),
            [.. GccClosing.AppendTo([Text("Where the hours go", "Invoices are keyed twice.")], closing), Text("People Also Ask", "It encrypts data at rest.")]);
        var storedJson = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var provider = new ReviseProvider();

        var revisedJson = await Build(provider).ReviseAsync(
            storedJson, "Say it plainer.", "document", null, ContentGeneratorProvider.OpenAi, CancellationToken.None, "pillar");

        // The writer was shown the draft without the page's closing, so it could neither rewrite it nor be
        // told to.
        Assert.Contains("Invoices are keyed twice.", provider.Sent, StringComparison.Ordinal);
        Assert.DoesNotContain("What business objective should this automation serve?", provider.Sent, StringComparison.Ordinal);
        Assert.DoesNotContain("booking your free consultation", provider.Sent, StringComparison.Ordinal);
        Assert.DoesNotContain("Answer these questions when", provider.Sent, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(revisedJson);
        var sections = doc.RootElement.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(2, sections.Count);
        Assert.Equal("People Also Ask", sections[1].GetProperty("heading").GetString());
        var first = sections[0].GetProperty("paragraphs").EnumerateArray().ToList();
        Assert.Equal(3, first.Count);
        Assert.Equal("list", first[^1].GetProperty("type").GetString());
        Assert.Equal(
            "booking your free consultation",
            first[1].GetProperty("runs").EnumerateArray().First(r => r.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String)
                .GetProperty("text").GetString());
        Assert.Equal(1, sections.Sum(s => s.GetProperty("paragraphs").EnumerateArray().Count(p =>
            p.GetProperty("type").GetString() == "text"
            && p.GetProperty("runs").EnumerateArray().Any(r => r.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String))));
    }

    [Fact]
    public async Task Revising_a_tool_page_reads_the_product_from_its_own_field_and_keeps_the_title_and_the_field()
    {
        var stored = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [
                new Section("h2", "Where the hours go", [new TextParagraph([new Run("Invoices are keyed twice.")])], null, []),
                new Section("h2", "People Also Ask", [new TextParagraph([new Run("It encrypts data at rest.")])], null, []),
            ]);
        var envelope = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Ramp: Automated Approval Workflows",
            ["productName"] = "Ramp",
            ["metaDescription"] = "A meta description.",
            ["body"] = stored,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var provider = new ReviseProvider();

        var revised = await Build(provider).ReviseAsync(
            envelope, "Say it plainer.", "document", null, ContentGeneratorProvider.OpenAi, CancellationToken.None, "tool");

        // The product is Ramp, so the writer is told about Ramp and the page's address; it is not
        // "Ramp: Automated Approval Workflows", whose slug is a different page.
        Assert.Contains("Ramp", provider.Sent, StringComparison.Ordinal);
        Assert.DoesNotContain("ramp-automated-approval-workflows", provider.Sent, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(revised);
        Assert.Equal("Ramp: Automated Approval Workflows", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Ramp", doc.RootElement.GetProperty("productName").GetString());
    }

    [Fact]
    public async Task A_pillar_whose_brief_has_no_questions_ends_on_the_plain_booking_line()
    {
        var provider = new ScriptedProvider();

        var (body, _) = await Pillar(provider, """{"paaQuestions":["Is it secure?"]}""");

        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        var last = sections[^2].GetProperty("paragraphs").EnumerateArray().Last();
        Assert.Equal("text", last.GetProperty("type").GetString());
        var runs = last.GetProperty("runs").EnumerateArray().ToList();
        Assert.Equal(["Book your free consultation", "."], runs.Select(r => r.GetProperty("text").GetString()));
        Assert.Equal(Scheduler, runs[0].GetProperty("href").GetString());
    }

    [Fact]
    public async Task A_batch_short_of_its_floor_and_its_keyword_is_reported_and_not_written_again()
    {
        // Every draft of 2026-10-05 scored 40 on its own SEO report. A batch is measured when it comes
        // back and what it is short of is said with the draft; the batch is not asked for again.
        var provider = new ScriptedProvider();

        var (body, warnings) = await Pillar(provider);

        Assert.Contains("Planned section A", body.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(warnings, w => w.Contains("word floor", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("has no heading containing", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("retry", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, provider.BodyCalls);
    }

    [Fact]
    public async Task A_draft_that_fails_a_refusing_check_is_refused_on_that_attempt()
    {
        var provider = new ScriptedProvider(heading: "Best AP automation tools");
        var service = Build(provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GeneratePillarBodyAsync(
            Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None));

        Assert.StartsWith("Refused: the pillar", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("retry", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, provider.BodyCalls);
    }

    [Fact]
    public async Task The_people_also_ask_section_is_on_the_draft()
    {
        var provider = new ScriptedProvider();
        const string brief = """{"paaQuestions":["Is it secure?"]}""";

        var (body, _) = await Pillar(provider, brief);

        Assert.Contains("People Also Ask", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_prompt_failure_saves_the_guarded_draft_and_reports_it()
    {
        var provider = new FailingImagePromptsProvider(new ScriptedProvider());

        var (body, warnings) = await Pillar(provider);

        Assert.Contains("Planned section A", body.GetRawText(), StringComparison.Ordinal);
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
