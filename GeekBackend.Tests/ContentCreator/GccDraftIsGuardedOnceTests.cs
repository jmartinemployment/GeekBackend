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
        // One call for each pair of body sections, and no second call for any of them.
        Assert.Equal(GeekAPI.Services.ContentCreator.ContentTypes.PillarPrompts.BodySectionCount / GeekAPI.Services.ContentCreator.GccGenerateService.SectionsPerBatch, provider.BodyCalls);
    }

    private const string BriefWithQuestions = """
        {"paaQuestions":["Is it secure?"],
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
    public async Task The_operators_questions_never_reach_the_writer()
    {
        var provider = new ScriptedProvider();

        await Pillar(provider, BriefWithQuestions);

        var prompts = provider.AllPrompts;
        string[] never =
        [
            "What business objective should this automation serve?", "How clean is the data",
            Scheduler, "CLOSING:",
        ];
        var found = never.Where(needle => prompts.Contains(needle, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 0, "The writer was sent: " + string.Join(" | ", found)
            + " -- " + string.Join(" || ", found.Select(f => prompts[Math.Max(0, prompts.IndexOf(f, StringComparison.Ordinal) - 80)..Math.Min(prompts.Length, prompts.IndexOf(f, StringComparison.Ordinal) + 120)])));
        Assert.Contains("END OF THE PAGE", prompts, StringComparison.Ordinal);
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
        // One call for each pair of body sections, and no second call for any of them.
        Assert.Equal(GeekAPI.Services.ContentCreator.ContentTypes.PillarPrompts.BodySectionCount / GeekAPI.Services.ContentCreator.GccGenerateService.SectionsPerBatch, provider.BodyCalls);
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
        // One call for each pair of body sections, and no second call for any of them.
        Assert.Equal(GeekAPI.Services.ContentCreator.ContentTypes.PillarPrompts.BodySectionCount / GeekAPI.Services.ContentCreator.GccGenerateService.SectionsPerBatch, provider.BodyCalls);
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
