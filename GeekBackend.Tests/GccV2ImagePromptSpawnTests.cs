using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests;

public sealed class GccV2ImagePromptSpawnTests
{
    [Fact]
    public void BuildTargets_pillar_includes_hero_and_h2s_excludes_faq()
    {
        var document = new ContentDocument(
            new Section("h2", "Opening", [], null, []),
            [
                new Section("h2", "Framework", [], null, []),
                new Section("h2", PillarSectionClassifier.FaqSectionTitle, [], null, []),
                new Section("h2", "Implementation", [], null, []),
            ]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets("pillar", "Enterprise AI Guide", document);

        Assert.Equal(3, targets.Count);
        Assert.Equal("pillar-hero", targets[0].SourceType);
        Assert.Equal(0, targets[0].Order);
        Assert.Equal("pillar", targets[1].SourceType);
        Assert.Equal("Framework", targets[1].Heading);
        Assert.Equal(1, targets[1].Order);
        Assert.Equal("Implementation", targets[2].Heading);
        Assert.DoesNotContain(targets, t => t.Heading == PillarSectionClassifier.FaqSectionTitle);
    }

    [Fact]
    public void BuildTargets_blog_includes_hero_and_body_sections()
    {
        var document = new ContentDocument(
            new Section("h2", "Opening", [], null, []),
            [new Section("h2", "Step one", [], null, [])]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets("blog", "Weekly update", document);

        Assert.Equal(2, targets.Count);
        Assert.Equal("blog-hero", targets[0].SourceType);
        Assert.Equal("blog", targets[1].SourceType);
    }

    [Theory]
    [InlineData("tool", "tool", 1)]
    [InlineData("email", "email", 0)]
    [InlineData("social", "social", 0)]
    [InlineData("ads", "ads", 0)]
    public void BuildTargets_short_form_spawns_one_companion(string contentType, string expectedType, int expectedOrder)
    {
        var document = new ContentDocument(new Section("h2", "Body", [], null, []), []);

        var targets = GccV2ImagePromptSpawnService.BuildTargets(contentType, "Companion title", document);

        Assert.Single(targets);
        Assert.Equal(expectedType, targets[0].SourceType);
        Assert.Equal(expectedOrder, targets[0].Order);
    }

    [Fact]
    public void ImagePromptExportSlug_uses_hero_and_h2_patterns()
    {
        var hero = GccV2HtmlExportService.ImagePromptExportSlug(
            "enterprise-ai",
            new ImagePromptSectionMeta(Guid.NewGuid(), "pillar-hero", "Enterprise AI Guide", 0));
        Assert.Equal("enterprise-ai-pillar-hero", hero);

        var h2 = GccV2HtmlExportService.ImagePromptExportSlug(
            "enterprise-ai",
            new ImagePromptSectionMeta(Guid.NewGuid(), "pillar", "Implementation Framework", 2));
        Assert.Equal("enterprise-ai-pillar-h2-implementation-framework", h2);
    }

    [Theory]
    [InlineData("pillar-hero", "image-prompts/pillar")]
    [InlineData("blog-hero", "image-prompts/blog")]
    [InlineData("pillar", "image-prompts/sections")]
    [InlineData("tool", "image-prompts/sections")]
    [InlineData("email", "image-prompts/email")]
    [InlineData("social", "image-prompts/social/linkedin")]
    [InlineData("ads", "image-prompts/ads")]
    public void ImagePromptFolderFor_maps_source_type(string sourceType, string expectedFolder)
    {
        Assert.Equal(expectedFolder, GccV2HtmlExportService.ImagePromptFolderFor(sourceType));
    }

    [Fact]
    public void ParseImagePromptSection_reads_brief_metadata()
    {
        var sourceJobId = Guid.NewGuid();
        var json = $$"""
            {
              "imagePromptSection": {
                "sourceJobId": "{{sourceJobId}}",
                "sourceType": "pillar",
                "heading": "Framework",
                "order": 2
              }
            }
            """;

        var meta = GccV2ImagePromptSpawnService.ParseImagePromptSection(json);

        Assert.NotNull(meta);
        Assert.Equal(sourceJobId, meta!.SourceJobId);
        Assert.Equal("pillar", meta.SourceType);
        Assert.Equal("Framework", meta.Heading);
        Assert.Equal(2, meta.Order);
    }
}
