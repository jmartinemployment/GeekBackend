using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2UnifiedRagTests
{
    [Fact]
    public void Mapper_covers_all_17_canonical_content_types()
    {
        Assert.Equal(17, GccV2ContentTypeRagMapper.CanonicalContentTypes.Count);
        foreach (var type in GccV2ContentTypeRagMapper.CanonicalContentTypes)
        {
            var route = GccV2ContentTypeRagMapper.Map(type);
            Assert.Equal(type, route.ContentType);
            Assert.Contains(route.WritingIntent, RagWritingIntents.All);
        }

        Assert.Equal(RagRetrievalFamily.Battlecard, GccV2ContentTypeRagMapper.Map("comparison").Family);
        Assert.Equal(RagRetrievalFamily.Slides, GccV2ContentTypeRagMapper.Map("linkedin-document").Family);
        Assert.True(GccV2ContentTypeRagMapper.Map("image-prompt").IsImagePrompt);
    }

    [Fact]
    public void Generation_brief_preserves_strategy_research_and_site_context()
    {
        var createId = Guid.NewGuid();
        var briefId = Guid.NewGuid();
        var crawlId = Guid.NewGuid();
        var raw = """
        {
          "title": "Unified RAG",
          "primaryIntent": "commercial_investigation",
          "buyingStage": "consideration",
          "toneOfVoice": "commercial_balanced",
          "paaQuestions": ["What is RAG?"],
          "competitorUrls": ["https://competitor.example/rag"],
          "operatorTools": [{"name":"Evidence Engine","url":"https://partner.example"}],
          "targetEntities": ["Evidence Engine"],
          "requiredTopics": ["verified citations"],
          "exclusions": ["unsupported claims"]
        }
        """;
        var brief = new GccV2BriefDto(briefId, createId, 1, "unified RAG", "blog", raw, null, DateTimeOffset.UtcNow);
        var create = new GccV2CreateDto(
            createId, Guid.NewGuid().ToString("D"), "Unified RAG", "blog", DateTimeOffset.UtcNow, null,
            """{"relatedPages":[{"url":"https://example.com/research"}]}""", "https://example.com", crawlId);
        var job = new GccV2JobDto(
            Guid.NewGuid(), "blog", briefId, create.OwnerUserId, createId, "plan", "running", 1, null, null,
            null, null, null, null, DateTimeOffset.UtcNow, null, null, null, crawlId);

        var assembled = GccV2GenerationBriefAssembler.Assemble(job, brief, create, null);
        var context = assembled.ToCanonicalBrief();

        Assert.Equal(GccV2GenerationBrief.CurrentVersion, assembled.Version);
        Assert.Equal("consideration", assembled.BuyingStage);
        Assert.Contains("verified citations", assembled.RequiredTopics);
        Assert.Contains("Evidence Engine", assembled.OperatorTools);
        Assert.Contains(crawlId.ToString(), context.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_policy_uses_stage_defaults_and_rejects_unapproved_override()
    {
        var policy = new ContentModelPolicy();
        var best = Brief("""{"modelPolicyPreset":"best-quality"}""");
        Assert.Equal(ContentModelPolicy.O1Pro, policy.Select(ContentGenerationStage.Outline, best).EffectiveModel);
        Assert.Equal(ContentModelPolicy.O3, policy.Select(ContentGenerationStage.Section, best).EffectiveModel);
        Assert.Equal(ContentModelPolicy.O1Pro, policy.Select(ContentGenerationStage.FinalSynthesis, best).EffectiveModel);

        var o3Only = Brief("""{"modelPolicyPreset":"o3-only","downgradeConfirmed":true}""");
        Assert.Equal(ContentModelPolicy.O3, policy.Select(ContentGenerationStage.Outline, o3Only).EffectiveModel);
        Assert.Equal(ContentModelPolicy.O3, policy.Select(ContentGenerationStage.FinalSynthesis, o3Only).EffectiveModel);

        var unconfirmed = Brief("""{"modelPolicy":{"version":"content-model-policy.v1","preset":"o3-only"}}""");
        var confirmationError = Assert.Throws<InvalidOperationException>(() =>
            policy.Select(ContentGenerationStage.Section, unconfirmed));
        Assert.Contains("downgradeConfirmed=true", confirmationError.Message, StringComparison.Ordinal);

        var nested = Brief("""
        {"modelPolicy":{"version":"content-model-policy.v1","preset":"custom","stageModels":{"Outline":"o3"},"downgradeConfirmed":true}}
        """);
        var nestedSelection = policy.Select(ContentGenerationStage.Outline, nested);
        Assert.Equal(ContentModelPreset.Custom, nestedSelection.Preset);
        Assert.Equal(ContentModelPolicy.O3, nestedSelection.EffectiveModel);

        var customWithoutFinal = Brief("""
        {"modelPolicy":{"version":"content-model-policy.v1","preset":"custom","stageModels":{"section":"o3"},"downgradeConfirmed":true}}
        """);
        Assert.Throws<InvalidOperationException>(() =>
            policy.Select(ContentGenerationStage.FinalSynthesis, customWithoutFinal));

        var invalid = Brief("""{"modelPolicyPreset":"custom","downgradeConfirmed":true,"modelPolicyOverrides":{"section":"gpt-4o-mini"}}""");
        var error = Assert.Throws<InvalidOperationException>(() =>
            policy.Select(ContentGenerationStage.Section, invalid));
        Assert.Contains("not approved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_section_parser_keeps_heading_and_body()
    {
        var section = GccV2WriteService.MarkdownToSection(
            "## Ignored upstream heading\n\nGrounded paragraph.\n\n- Evidence one\n- Evidence two",
            "Canonical heading");

        Assert.Equal("Canonical heading", section.Heading);
        Assert.Equal("h2", section.Tag);
        Assert.Equal(2, section.Paragraphs.Count);
    }

    [Fact]
    public void Final_synthesis_markdown_round_trip_preserves_order_keys_lists_and_links()
    {
        var lede = new GccV2WriteSection(
            "lede", "Introduction", "problem",
            new Section("h2", "Introduction",
            [
                new TextParagraph([new Run("Read "), new Run("evidence", Href: "https://fixture.test")]),
            ], null, []),
            false);
        var body = new GccV2WriteSection(
            "proof", "Verified Proof", "proof",
            new Section("h2", "Verified Proof",
            [
                new ListParagraph(false, [[new Run("First")], [new Run("Second")]]),
            ], null, []),
            false);
        var output = new GccV2WriteOutput
        {
            Title = "Stable Draft",
            MetaDescription = "Meta",
            Lede = lede,
            Sections = [body],
        };

        var markdown = GccV2WriteService.ToStableMarkdown(output);
        var parsed = GccV2WriteService.ParseSynthesizedMarkdown(markdown, output.AllSections);

        Assert.Equal(["Introduction", "Verified Proof"], parsed.Select(s => s.Heading));
        var link = Assert.IsType<TextParagraph>(parsed[0].Paragraphs[0]).Runs[1];
        Assert.Equal("https://fixture.test", link.Href);
        Assert.Equal(2, Assert.IsType<ListParagraph>(parsed[1].Paragraphs[0]).Items.Count);
        Assert.Equal(["lede", "proof"], output.AllSections.Select(s => s.SectionKey));
    }

    [Theory]
    [InlineData("# Draft\n\n## Introduction\n\nBody.")]
    [InlineData("# Draft\n\n## Changed Introduction\n\nBody.\n\n## Verified Proof\n\nBody.")]
    public void Final_synthesis_rejects_missing_or_mutated_headings(string markdown)
    {
        var expected = new[]
        {
            new GccV2WriteSection("lede", "Introduction", "problem",
                new Section("h2", "Introduction", [new TextParagraph([new Run("Body")])], null, []), false),
            new GccV2WriteSection("proof", "Verified Proof", "proof",
                new Section("h2", "Verified Proof", [new TextParagraph([new Run("Body")])], null, []), false),
        };

        Assert.Throws<InvalidOperationException>(() =>
            GccV2WriteService.ParseSynthesizedMarkdown(markdown, expected));
    }

    private static GccV2GenerationBrief Brief(string raw)
    {
        var createId = Guid.NewGuid();
        var dto = new GccV2BriefDto(
            Guid.NewGuid(), createId, 1, "topic", "blog", raw, null, DateTimeOffset.UtcNow);
        var job = new GccV2JobDto(
            Guid.NewGuid(), "blog", dto.Id, Guid.NewGuid().ToString("D"), createId,
            "plan", "running", 1, null, null, null, null, null, null,
            DateTimeOffset.UtcNow, null, null);
        return GccV2GenerationBriefAssembler.Assemble(job, dto, null, null);
    }
}
