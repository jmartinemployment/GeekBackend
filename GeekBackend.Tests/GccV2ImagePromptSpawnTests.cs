using GeekAPI.Services.ContentCreatorV2.ContentTypes;
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

    [Fact]
    public void BuildTargets_tool_is_first_class_hero_plus_h2s()
    {
        var document = new ContentDocument(
            new Section("h2", "Overview lede", [], null, []),
            [
                new Section("h2", "Pricing", [], null, []),
                new Section("h2", PillarSectionClassifier.FaqSectionTitle, [], null, []),
                new Section("h2", "Integrations", [], null, []),
            ]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets("tool", "Partner AI Writer", document);

        Assert.Equal(3, targets.Count);
        Assert.Equal("tool-hero", targets[0].SourceType);
        Assert.Equal(0, targets[0].Order);
        Assert.Equal("Partner AI Writer", targets[0].Heading);
        Assert.Equal("tool", targets[1].SourceType);
        Assert.Equal("Pricing", targets[1].Heading);
        Assert.Equal("Integrations", targets[2].Heading);
        Assert.DoesNotContain(targets, t => t.Heading == PillarSectionClassifier.FaqSectionTitle);
        Assert.False(targets.Count == 1, "tool must not remain single-companion");
    }

    [Theory]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("ads")]
    [InlineData("linkedin-document")]
    public void BuildTargets_channel_types_get_hero_and_h2s_when_present(string contentType)
    {
        var document = new ContentDocument(
            new Section("h2", "Lede", [], null, []),
            [
                new Section("h2", "Section A", [], null, []),
                new Section("h2", "Section B", [], null, []),
            ]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets(contentType, "Channel title", document);

        Assert.Equal(3, targets.Count);
        Assert.Equal($"{contentType}-hero", targets[0].SourceType);
        Assert.Equal(0, targets[0].Order);
        Assert.Equal(contentType, targets[1].SourceType);
        Assert.Equal("Section A", targets[1].Heading);
        Assert.Equal("Section B", targets[2].Heading);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("ads")]
    [InlineData("linkedin-document")]
    public void BuildTargets_channel_types_hero_at_minimum_without_body_h2s(string contentType)
    {
        var document = new ContentDocument(new Section("h2", "Lede only", [], null, []), []);

        var targets = GccV2ImagePromptSpawnService.BuildTargets(contentType, "Channel title", document);

        Assert.Single(targets);
        Assert.Equal($"{contentType}-hero", targets[0].SourceType);
        Assert.Equal(0, targets[0].Order);
    }

    [Fact]
    public void BuildTargets_image_prompt_parent_spawns_zero()
    {
        var document = new ContentDocument(
            new Section("h2", "Visual", [], null, []),
            [new Section("h2", "More", [], null, [])]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets("image-prompt", "Visual brief", document);

        Assert.Empty(targets);
        Assert.False(GccV2LongFormTypes.UsesHeroAndSectionImagePrompts("image-prompt"));
    }

    [Fact]
    public void BuildTargets_skips_duplicate_title_h1_and_non_heading_tags()
    {
        var document = new ContentDocument(
            new Section("h2", "Lede", [], null, []),
            [
                new Section("h1", "Same Title", [], null, []),
                new Section("h1", "Extra H1", [], null, []),
                new Section("p", "Not a heading section", [], null, []),
                new Section("", "Missing tag counts as H2", [], null, []),
            ]);

        var targets = GccV2ImagePromptSpawnService.BuildTargets("blog", "Same Title", document);

        Assert.Equal(3, targets.Count);
        Assert.Equal("blog-hero", targets[0].SourceType);
        Assert.Contains(targets, t => t.Heading == "Extra H1");
        Assert.Contains(targets, t => t.Heading == "Missing tag counts as H2");
        Assert.DoesNotContain(targets, t => t.Heading == "Same Title" && t.Order > 0);
        Assert.DoesNotContain(targets, t => t.Heading == "Not a heading section");
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

        var toolH2 = GccV2HtmlExportService.ImagePromptExportSlug(
            "partner-writer",
            new ImagePromptSectionMeta(Guid.NewGuid(), "tool", "Pricing", 1));
        Assert.Equal("partner-writer-tool-h2-pricing", toolH2);
    }

    [Theory]
    [InlineData("pillar-hero", "image-prompts/pillar")]
    [InlineData("blog-hero", "image-prompts/blog")]
    [InlineData("tool-hero", "image-prompts/tool")]
    [InlineData("pillar", "image-prompts/sections")]
    [InlineData("tool", "image-prompts/sections")]
    [InlineData("email", "image-prompts/email")]
    [InlineData("email-hero", "image-prompts/email")]
    [InlineData("social", "image-prompts/social/linkedin")]
    [InlineData("ads", "image-prompts/ads")]
    [InlineData("linkedin-document", "image-prompts/linkedin-document")]
    [InlineData("linkedin-document-hero", "image-prompts/linkedin-document")]
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

    [Fact]
    public void SpawnResult_not_applicable_for_non_spawn_content_types()
    {
        var result = new SpawnResult(0, 0, null, null);
        Assert.True(result.NotApplicable);
    }

    [Fact]
    public void SpawnResult_failure_is_not_not_applicable()
    {
        var result = new SpawnResult(0, 0, "Source job has no ResultJson.", null);
        Assert.False(result.NotApplicable);
    }

    [Fact]
    public void SpawnResult_skip_reason_is_not_not_applicable()
    {
        var result = new SpawnResult(0, 0, null, "No image-prompt targets for content type 'tool'.");
        Assert.False(result.NotApplicable);
    }

    [Theory]
    [InlineData("tool", true)]
    [InlineData("email", true)]
    [InlineData("linkedin-document", true)]
    [InlineData("image-prompt", false)]
    public void UsesHeroAndSectionImagePrompts_covers_all_parents(string contentType, bool expected)
    {
        Assert.Equal(expected, GccV2LongFormTypes.UsesHeroAndSectionImagePrompts(contentType));
    }
}
