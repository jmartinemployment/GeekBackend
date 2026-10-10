using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Revise writes in the same two-section calls the page was written in (Jeff, 2026-10-10).
/// </summary>
/// <remarks>
/// It asked one call for the whole body. A body call writes about 650 words whatever it is asked, so a
/// long page came back at a fraction of itself and was refused as a rewrite, every time; with ten body
/// sections it could not have worked at all. Each call is now shown the whole page, assigned two of its
/// sections and returns those two. A revision of one section is one call, and every other section is the
/// stored one, untouched.
/// </remarks>
public sealed class GccReviseInBatchesTests
{
    private const string Filler =
        "This sentence stands in for a paragraph of body text and it is long enough to be counted as words.";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Section Body(string heading, string firstSentence, params Section[] children) =>
        new("h2", heading, [new TextParagraph([new Run($"{firstSentence} {Filler}")])], null, children, Provenance: "plan");

    /// <summary>An opening and <paramref name="count"/> body sections, "Section 1" to "Section N".</summary>
    private static string Page(int count, params (int Index, Section Child)[] children)
    {
        var sections = Enumerable.Range(1, count)
            .Select(n => Body(
                $"Section {n}",
                $"Section {n} says its own thing.",
                [.. children.Where(c => c.Index == n).Select(c => c.Child)]))
            .ToList();
        var document = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []), sections);
        return JsonSerializer.Serialize(document, Web);
    }

    /// <summary>
    /// Answers each call with the sections it was assigned, read from the prompt's YOUR SECTIONS block: the
    /// same headings, each marked revised. <paramref name="answer"/> replaces that reply.
    /// </summary>
    private sealed class AssignedSectionsProvider(Func<IReadOnlyList<string>, string>? answer = null) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public List<string> Prompts { get; } = [];

        public List<IReadOnlyList<string>> Assigned { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var prompt = string.Join("\n", request.Messages.Select(m => m.Content));
            Prompts.Add(prompt);

            var lines = prompt.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var start = lines.FindIndex(line => line.StartsWith("=== YOUR SECTIONS", StringComparison.Ordinal));
            var headings = lines.Skip(start + 1)
                .TakeWhile(line => line.StartsWith("[H2] ", StringComparison.Ordinal))
                .Select(line => line["[H2] ".Length..])
                .ToList();
            Assigned.Add(headings);

            var content = answer?.Invoke(headings) ?? Reply(headings.Select(h => (h, $"{h} revised. {Filler}")));
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        internal static string Reply(IEnumerable<(string Heading, string Text)> sections) =>
            JsonSerializer.Serialize(new
            {
                sections = sections.Select(s => new
                {
                    tag = "h2",
                    heading = s.Heading,
                    paragraphs = new[] { new { type = "text", runs = new[] { new { text = s.Text } } } },
                    href = (string?)null,
                    children = Array.Empty<object>(),
                    provenance = "plan",
                }),
            });
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

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

    private static Task<string> Revise(
        IContentGenerationProvider provider, string page, string scope = "document", string? section = null, string type = "pillar") =>
        Build(provider).ReviseAsync(
            page, "Say it plainer.", scope, section, ContentGeneratorProvider.OpenAi, CancellationToken.None, type);

    private static List<JsonElement> SectionsOf(JsonDocument doc) =>
        [.. doc.RootElement.GetProperty("sections").EnumerateArray()];

    private static string FirstText(JsonElement section) =>
        section.GetProperty("paragraphs")[0].GetProperty("runs")[0].GetProperty("text").GetString()!;

    // ---- the whole page ------------------------------------------------------------------------------

    [Theory]
    [InlineData("pillar")]
    [InlineData("tool")]
    [InlineData("blog")]
    public async Task A_ten_section_page_is_revised_in_five_calls_of_two_sections(string type)
    {
        var provider = new AssignedSectionsProvider();

        var revisedJson = await Revise(provider, Page(10), type: type);

        Assert.Equal(5, provider.Prompts.Count);
        Assert.Equal(
            [
                ["Section 1", "Section 2"], ["Section 3", "Section 4"], ["Section 5", "Section 6"],
                ["Section 7", "Section 8"], ["Section 9", "Section 10"],
            ],
            provider.Assigned);

        using var doc = JsonDocument.Parse(revisedJson);
        var sections = SectionsOf(doc);
        Assert.Equal(Enumerable.Range(1, 10).Select(n => $"Section {n}"), sections.Select(s => s.GetProperty("heading").GetString()));
        Assert.All(sections, s => Assert.Contains("revised.", FirstText(s), StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_odd_last_section_gets_a_call_of_its_own()
    {
        var provider = new AssignedSectionsProvider();

        await Revise(provider, Page(5));

        Assert.Equal([["Section 1", "Section 2"], ["Section 3", "Section 4"], ["Section 5"]], provider.Assigned);
    }

    [Fact]
    public async Task Every_call_is_shown_the_whole_page_and_told_to_return_only_its_own_sections()
    {
        var provider = new AssignedSectionsProvider();

        await Revise(provider, Page(10));

        // The third call revises sections 5 and 6 and can read what comes before and after them.
        var third = provider.Prompts[2];
        Assert.Contains("=== THE DRAFT YOU ARE REVISING ===", third, StringComparison.Ordinal);
        Assert.Contains("Section 1 says its own thing.", third, StringComparison.Ordinal);
        Assert.Contains("Section 4 says its own thing.", third, StringComparison.Ordinal);
        Assert.Contains("Section 7 says its own thing.", third, StringComparison.Ordinal);
        Assert.Contains("Section 10 says its own thing.", third, StringComparison.Ordinal);
        Assert.Contains("Do not return it. Return only the sections listed under YOUR SECTIONS", third, StringComparison.Ordinal);
        Assert.Contains("=== YOUR SECTIONS (return exactly these, in this order, and nothing else) ===", third, StringComparison.Ordinal);
        Assert.Contains("Say it plainer.", third, StringComparison.Ordinal);
        // The page as it stands, never a later call's rewrite of it: a call revises what the operator saw.
        Assert.DoesNotContain("Section 1 revised.", third, StringComparison.Ordinal);
    }

    // ---- one section -----------------------------------------------------------------------------------

    [Fact]
    public async Task Revising_one_section_is_one_call_and_every_other_section_is_the_stored_one_untouched()
    {
        var provider = new AssignedSectionsProvider();
        var page = Page(10);

        var revisedJson = await Revise(provider, page, scope: "section", section: "  section 7 ");

        Assert.Equal([["Section 7"]], provider.Assigned);

        using var before = JsonDocument.Parse(page);
        using var after = JsonDocument.Parse(revisedJson);
        var was = SectionsOf(before);
        var now = SectionsOf(after);
        Assert.Equal(10, now.Count);
        for (var i = 0; i < 10; i++)
        {
            if (i == 6)
            {
                Assert.StartsWith("Section 7 revised.", FirstText(now[i]), StringComparison.Ordinal);
                continue;
            }

            Assert.Equal(was[i].GetRawText(), now[i].GetRawText());
        }
    }

    [Fact]
    public async Task Naming_a_subsection_revises_the_section_that_holds_it_and_says_which_part()
    {
        var provider = new AssignedSectionsProvider();
        var child = new Section("h3", "The first step", [new TextParagraph([new Run($"The invoice arrives. {Filler}")])], null, []);

        await Revise(provider, Page(4, (3, child)), scope: "section", section: "the first step");

        Assert.Equal([["Section 3"]], provider.Assigned);
        var prompt = Assert.Single(provider.Prompts);
        Assert.Contains("Revise ONLY the part headed \"the first step\" within it.", prompt, StringComparison.Ordinal);
        Assert.Contains("everything else in it exactly as it stands", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_that_is_no_heading_on_the_page_is_refused_naming_the_headings_and_nothing_is_asked()
    {
        var provider = new AssignedSectionsProvider();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Revise(provider, Page(3), scope: "section", section: "Pricing"));

        Assert.StartsWith("Refused: no section of this page is headed \"Pricing\"", refused.Message, StringComparison.Ordinal);
        Assert.Contains("\"Section 1\"; \"Section 2\"; \"Section 3\"", refused.Message, StringComparison.Ordinal);
        Assert.Empty(provider.Prompts);
    }

    [Fact]
    public async Task A_section_revision_still_needs_the_sections_name()
    {
        var provider = new AssignedSectionsProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Revise(provider, Page(3), scope: "section", section: "  "));

        Assert.Empty(provider.Prompts);
    }

    // ---- what is refused -------------------------------------------------------------------------------

    [Fact]
    public async Task A_call_that_returns_fewer_sections_than_it_was_sent_is_refused()
    {
        var provider = new AssignedSectionsProvider(
            headings => AssignedSectionsProvider.Reply([(headings[0], $"{headings[0]} revised. {Filler}")]));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Revise(provider, Page(4)));

        Assert.StartsWith("Refused: the revision of \"Section 1\", \"Section 2\" came back as 1 section(s) where 2 were sent.", refused.Message, StringComparison.Ordinal);
        // Refused on the call that broke it: the second call is never made.
        Assert.Single(provider.Prompts);
    }

    [Fact]
    public async Task A_call_that_returns_a_section_nobody_assigned_is_refused()
    {
        var provider = new AssignedSectionsProvider(
            headings => AssignedSectionsProvider.Reply(
                headings.Select(h => (h, $"{h} revised. {Filler}")).Append(("An extra section", $"Extra. {Filler}"))));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Revise(provider, Page(4)));

        Assert.Contains("came back as 3 section(s) where 2 were sent", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revision_that_comes_back_a_quarter_shorter_is_still_refused_on_the_whole_page()
    {
        // Every call returns its two sections, each cut to a few words.
        var provider = new AssignedSectionsProvider(
            headings => AssignedSectionsProvider.Reply(headings.Select(h => (h, "Cut short."))));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Revise(provider, Page(10)));

        Assert.StartsWith("Refused: the revision came back at", refused.Message, StringComparison.Ordinal);
        Assert.Contains("The current version is unchanged.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_with_no_body_sections_is_refused_and_nothing_is_asked()
    {
        var provider = new AssignedSectionsProvider();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Revise(provider, Page(0)));

        Assert.Contains("no body sections", refused.Message, StringComparison.Ordinal);
        Assert.Empty(provider.Prompts);
    }

    // ---- what a revision call is told about length -------------------------------------------------------

    [Fact]
    public async Task A_tool_revision_call_is_never_told_it_owes_zero_words()
    {
        // Sections assigned by their headings carry no floor of their own.
        var provider = new AssignedSectionsProvider();

        await Revise(provider, Page(2), type: "tool");

        Assert.DoesNotContain("at least 0 words", Assert.Single(provider.Prompts), StringComparison.Ordinal);
    }
}
