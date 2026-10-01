using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests;

public sealed class GccV2PartnerToolWriteTests
{
    [Fact]
    public void RenderSourceAttribution_emits_blockquote_cite_typographic_quotes_and_visit_link()
    {
        const string url = "https://botpenguin.com/product";
        const string quote = "BotPenguin automates conversational support for marketing teams.";
        var html = GccV2ToolSectionRenderer.RenderSourceAttribution(url, quote, "BotPenguin");

        Assert.Contains($"<blockquote cite=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains(
            $"<p>\u201C{quote}\u201D</p>",
            html,
            StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">Visit BotPenguin</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBlockQuoteText_wraps_verbatim_text_in_typographic_quotes()
    {
        var formatted = GccV2ToolSectionRenderer.FormatBlockQuoteText(
            "Pipedrive is a sales CRM built for small teams.");
        Assert.Equal("\u201CPipedrive is a sales CRM built for small teams.\u201D", formatted);
    }

    /// <summary>
    /// Extraction returns the structured fields and no quotation, 2026-10-01.
    /// </summary>
    /// <remarks>
    /// Five tests here used to pin the opposite, each on one step of a second way to choose a
    /// quotation: the first paragraph over 40 characters, the longest one with the page title in the
    /// running, a 500-character truncation with an ellipsis appended, and a failover to the model's own
    /// retyped sentence checked by substring. The input was <c>GccQuoteablePage.Paragraphs</c> —
    /// <c>RenderChunk</c> output — so the span could be a prompt label in a quote box attributed to a
    /// partner. The chain is gone; so are those tests, because what they pinned is the defect.
    ///
    /// <para>
    /// A quotable span is chosen in one place: <c>GccQuoteCandidates</c>, over a partner page's typed
    /// blocks, the model answering by candidate number.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Extraction_returns_the_structured_fields_and_never_a_quotation()
    {
        var provider = new ExtractionProvider();
        var extractor = new GccV2ToolResearchExtractor(
            new GccV2ToolPagePromptBuilder(), NullLogger<GccV2ToolResearchExtractor>.Instance);
        var page = new GccQuoteablePage(
            "https://pipedrive.com/crm", "Pipedrive CRM", [],
            [
                "Section: Why Pipedrive",
                "Pipedrive is a sales-focused CRM that helps small teams manage pipelines and close deals faster.",
            ]);

        var research = await extractor.ExtractAsync(
            provider, "Pipedrive", page.Url, [page], CancellationToken.None);

        Assert.NotNull(research);
        Assert.Equal("Sales CRM for small teams.", research!.Summary);
        // The model answered with one, and it is dropped: whatever it writes here is its own wording
        // about the partner, which is the one thing a quote box must never carry.
        Assert.Equal("", research.SourceQuote);
    }

    [Fact]
    public async Task Extraction_no_longer_refuses_for_want_of_a_quotation()
    {
        // It used to pick a quote before the model was called and throw if the pick came back empty,
        // so a partner page whose paragraphs were all short decided whether extraction ran at all.
        // The requirement that the page carry a blockquote is enforced where the page is built --
        // RequireSourceAttributionHtml below -- not by refusing to read the partner's data.
        var provider = new ExtractionProvider();
        var extractor = new GccV2ToolResearchExtractor(
            new GccV2ToolPagePromptBuilder(), NullLogger<GccV2ToolResearchExtractor>.Instance);
        var page = new GccQuoteablePage(
            "https://pipedrive.com/crm", "Pipedrive CRM", [], ["Pipedrive helps sales teams win."]);

        var research = await extractor.ExtractAsync(
            provider, "Pipedrive", page.Url, [page], CancellationToken.None);

        Assert.NotNull(research);
        Assert.Equal("Sales CRM for small teams.", research!.Summary);
    }

    private sealed class ExtractionProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatCompletionResult(
                """
                {"name":"Pipedrive","summary":"Sales CRM for small teams.",
                 "whatItDoes":"Manages pipelines.","features":["Pipelines"],"useCases":["Sales"],
                 "positioning":"Small teams.","pricing":"From $14.","sourceQuote":"Pipedrive is the best CRM anywhere."}
                """,
                "fake", 0, 0));
    }

    [Fact]
    public void The_partner_tool_write_path_refuses_rather_than_choose_a_quotation_its_own_way()
    {
        // It resolved one: first paragraph over 40 characters out of RenderChunk output, then a
        // failover to the model's retyped sentence. Both gone. This path has no typed passages, so it
        // has no quote source, and it says which one it would need instead of keeping a worse one.
        var ex = Assert.Throws<ContentGenerationException>(() =>
            GccV2PartnerToolWriteService.BuildAttributionQuote("Pipedrive", "https://pipedrive.com/crm"));

        Assert.Contains("GccQuoteCandidates", ex.Message, StringComparison.Ordinal);
        Assert.Contains("typed blocks", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_source_url_asks_for_no_quotation_at_all()
    {
        // A tool page with no partner URL has nothing to attribute, which is not a failure.
        Assert.Equal(("", 0), GccV2PartnerToolWriteService.BuildAttributionQuote("Pipedrive", null));
    }

    [Fact]
    public void RequireSourceAttributionHtml_emits_blockquote_and_visit_link()
    {
        const string url = "https://pipedrive.com/product";
        const string quote = "Pipedrive is a sales-focused CRM built for growing teams.";
        var html = GccV2PartnerToolWriteService.RequireSourceAttributionHtml(url, quote, "Pipedrive");

        Assert.Contains("<blockquote", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"cite=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains("Visit Pipedrive", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSourceAttributionHtml_throws_when_quote_missing()
    {
        const string url = "https://pipedrive.com/product";
        var ex = Assert.Throws<ContentGenerationException>(() =>
            GccV2PartnerToolWriteService.RequireSourceAttributionHtml(url, "", "Pipedrive"));
        Assert.Contains("verbatim source blockquote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPartnerToolSectionPrompt_includes_implementation_guidance()
    {
        var builder = new GccV2ToolPagePromptBuilder();
        var context = new ProjectGenerationContext(
            ProjectName: "CRM",
            ProjectUrl: "https://example.com",
            TargetKeyword: "CRM automation",
            Department: "marketing",
            SiteName: "Example Co",
            DetectedTone: "Professional",
            DetectedFocus: "CRM",
            CrawledHeadings: [],
            CrawledParagraphs: [],
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: [],
            PublisherName: "Example Co",
            PublisherLogoUrl: "https://example.com/logo.png",
            AuthorName: "Author",
            ArticleBaseUrl: "https://example.com",
            BlogBaseUrl: "https://example.com/blog",
            ToolBaseUrl: "https://example.com/tools",
            ImplementerPositioning: "AI implementer",
            Provider: LlmProviderType.OpenAi,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null);
        var metadata = new ArticleMetadataDraft("CRM", "Meta", ["CRM"], []);
        var app = new SoftwareApplicationDescriptor("Pipedrive", "CRM tool", null);

        var request = builder.BuildPartnerToolSectionPrompt(
            context,
            metadata,
            app,
            "pipedrive",
            "Implementation Considerations",
            2,
            4,
            null,
            null);

        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Accelerated deployment", system, StringComparison.Ordinal);
        Assert.Contains("Data model design", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolPageSchemaBuilder_subjectOf_uses_pillar_url_not_tool_url()
    {
        const string pillarUrl = "https://example.com/marketing/ai-chatbots";
        const string toolUrl = "https://example.com/tools/marketing/bot-penguin";
        var metadata = new ContentMetadata(
            "BotPenguin",
            "Tool meta",
            "Author",
            "Publisher",
            "https://example.com/logo.png",
            toolUrl,
            "https://example.com/logo.png",
            DateTime.UtcNow,
            DateTime.UtcNow,
            ["AI Chatbots"],
            1200);

        var json = GccV2ToolPageSchemaBuilder.BuildToolPage(
            metadata,
            pillarUrl,
            new SoftwareApplicationDescriptor(
                "BotPenguin",
                "Tool meta",
                toolUrl));

        using var document = JsonDocument.Parse(json);
        var subjectOf = document.RootElement.GetProperty("subjectOf");
        Assert.Equal(pillarUrl, subjectOf.GetProperty("@id").GetString());
    }
}
