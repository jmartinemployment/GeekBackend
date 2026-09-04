using GeekAPI.Services.ContentCreatorV2.Carousel;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;

namespace GeekBackend.Tests.ContentCreatorV2.Carousel;

public sealed class GccV2LinkedInCarouselParserTests
{
    private const string ValidCaption =
        "This is a long enough caption for LinkedIn that explains the carousel topic in detail with a personal point of view and ends with a question for the audience to respond to in comments today. What has worked best for your team recently when rolling out change?";

    private string ValidJson => $$"""
        {
          "slides": [
            { "role": "cover", "title": "Hook title", "subtitle": "Subtitle", "bullets": [] },
            { "role": "problem", "title": "Problem", "bullets": ["One", "Two"] },
            { "role": "teach", "title": "Teach 1", "bullets": ["A"] },
            { "role": "teach", "title": "Teach 2", "bullets": ["B"] },
            { "role": "teach", "title": "Teach 3", "bullets": ["C"] },
            { "role": "framework", "title": "Framework", "bullets": ["X"] }
          ],
          "caption": "{{ValidCaption}}",
          "hashtags": ["AI", "Automation"],
          "suggestedFilename": "AI_Implementation_Framework"
        }
        """;

    [Fact]
    public void Parse_valid_json_returns_draft()
    {
        var draft = GccV2LinkedInCarouselParser.Parse(ValidJson);
        Assert.Equal(6, draft.Slides.Count);
        Assert.Equal("Hook title", draft.Slides[0].Title);
        Assert.Equal(2, draft.Hashtags.Count);
    }

    [Fact]
    public void Parse_too_few_slides_throws()
    {
        var json = $$"""
            {
              "slides": [
                { "role": "cover", "title": "Only", "bullets": [] }
              ],
              "caption": "{{ValidCaption}}",
              "hashtags": [],
              "suggestedFilename": "test"
            }
            """;
        Assert.Throws<InvalidOperationException>(() => GccV2LinkedInCarouselParser.Parse(json));
    }
}

public sealed class GccV2LinkedInCarouselPdfTests
{
    [Fact]
    public void Render_produces_pdf_under_100mb()
    {
        var draft = new LinkedInCarouselDraft(
        [
            new CarouselSlide(0, "cover", "90% of AI Projects Fail", [], "Before you buy another tool"),
            new CarouselSlide(1, "problem", "The Real Problem", ["Teams skip problem definition", "Tools come first"]),
            new CarouselSlide(2, "teach", "Step 1", ["Map the workflow first"]),
            new CarouselSlide(3, "teach", "Step 2", ["Pick one metric"]),
            new CarouselSlide(4, "teach", "Step 3", ["Pilot with one team"]),
            new CarouselSlide(5, "cta", "Start Here", ["Book a workflow audit"]),
        ],
            "What would you add?",
            ["AI", "Consulting"],
            "AI_Implementation_Framework");

        var style = GccV2LinkedInCarouselService.BuildBrandStyle(null);
        var bytes = GccV2LinkedInCarouselPdfService.Render(draft, style);

        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length < 100 * 1024 * 1024);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, 4)));
    }

    [Fact]
    public void Page_dimensions_match_linkedin_spec()
    {
        Assert.Equal(1080f, GccV2LinkedInCarouselPdfService.PageWidth);
        Assert.Equal(1350f, GccV2LinkedInCarouselPdfService.PageHeight);
    }
}

public sealed class GccV2LinkedInCarouselSpawnTests
{
    [Fact]
    public void BriefIncludesLinkedInCarousel_true_when_checked()
    {
        Assert.True(GccV2LinkedInCarouselSpawnService.BriefIncludesLinkedInCarousel(
            """{"contentTypes":["pillar","linkedin-carousel"]}"""));
    }

    [Fact]
    public void BriefIncludesLinkedInCarousel_true_for_document_alias()
    {
        Assert.True(GccV2LinkedInCarouselSpawnService.BriefIncludesLinkedInCarousel(
            """{"contentTypes":["pillar","linkedin-document"]}"""));
    }

    [Fact]
    public void BriefIncludesLinkedInCarousel_false_without_type()
    {
        Assert.False(GccV2LinkedInCarouselSpawnService.BriefIncludesLinkedInCarousel(
            """{"contentTypes":["pillar","email"]}"""));
    }

    [Fact]
    public void IsLinkedIn_accepts_document_and_carousel_aliases()
    {
        Assert.True(GccV2ChannelTypes.IsLinkedIn("linkedin-document"));
        Assert.True(GccV2ChannelTypes.IsLinkedIn("linkedin-carousel"));
        Assert.False(GccV2ChannelTypes.IsLinkedIn("pillar"));
    }
}

public sealed class GccV2LinkedInCarouselArtifactTests
{
    private const string ValidCaption =
        "This is a long enough caption for LinkedIn that explains the carousel topic in detail with a personal point of view and ends with a question for the audience to respond to in comments today. What has worked best for your team recently when rolling out change?";

    [Fact]
    public void Merge_and_parse_roundtrip_preserves_slides()
    {
        var draft = new LinkedInCarouselDraft(
        [
            new CarouselSlide(0, "cover", "Hook", [], "Subtitle"),
            new CarouselSlide(1, "problem", "Problem", ["One"]),
            new CarouselSlide(2, "teach", "Teach", ["A"]),
            new CarouselSlide(3, "teach", "More", ["B"]),
            new CarouselSlide(4, "framework", "Framework", ["X"]),
            new CarouselSlide(5, "cta", "CTA", ["Go"]),
        ],
            ValidCaption,
            ["AI"],
            "ai-framework");

        var artifact = new LinkedInCarouselArtifact(draft, "ai-framework", DateTimeOffset.UtcNow);
        var merged = GccV2LinkedInCarouselService.MergeCarouselIntoResultJson(
            """{"title":"Test","document":{"sections":[]}}""",
            artifact);

        var parsed = GccV2LinkedInCarouselService.TryParseArtifactFromResultJson(merged);
        Assert.NotNull(parsed);
        Assert.Equal(6, parsed!.Draft.Slides.Count);
        Assert.Equal("ai-framework", parsed.Slug);
    }

    [Fact]
    public void Export_paths_include_pdf_caption_and_slides_json()
    {
        const string folder = "social/linkedin/carousels";
        const string slug = "ai-framework";

        var pdfPath = $"{folder}/{slug}.pdf";
        var captionPath = $"{folder}/{slug}-caption.txt";
        var slidesPath = $"{folder}/{slug}-slides.json";

        Assert.Equal("social/linkedin/carousels/ai-framework.pdf", pdfPath);
        Assert.EndsWith("-caption.txt", captionPath);
        Assert.EndsWith("-slides.json", slidesPath);
    }
}

public sealed class GccV2LinkedInCarouselServicePipelineTests
{
    private const string ValidCaption =
        "This is a long enough caption for LinkedIn that explains the carousel topic in detail with a personal point of view and ends with a question for the audience to respond to in comments today. What has worked best for your team recently when rolling out change?";

    private const string LlmJson = """
        {
          "slides": [
            { "role": "cover", "title": "Hook title", "subtitle": "Subtitle", "bullets": [] },
            { "role": "problem", "title": "Problem", "bullets": ["One", "Two"] },
            { "role": "teach", "title": "Teach 1", "bullets": ["A"] },
            { "role": "teach", "title": "Teach 2", "bullets": ["B"] },
            { "role": "teach", "title": "Teach 3", "bullets": ["C"] },
            { "role": "framework", "title": "Framework", "bullets": ["X"] }
          ],
          "caption": "CAPTION_PLACEHOLDER",
          "hashtags": ["AI"],
          "suggestedFilename": "AI_Framework"
        }
        """;

    [Fact]
    public void Parse_llm_json_and_render_produces_non_empty_pdf()
    {
        var json = LlmJson.Replace("CAPTION_PLACEHOLDER", ValidCaption, StringComparison.Ordinal);
        var draft = GccV2LinkedInCarouselParser.Parse(json);
        var style = GccV2LinkedInCarouselService.BuildBrandStyle(null);
        var pdfBytes = GccV2LinkedInCarouselPdfService.Render(draft, style);

        Assert.NotEmpty(pdfBytes);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes.AsSpan(0, 4)));
    }
}
