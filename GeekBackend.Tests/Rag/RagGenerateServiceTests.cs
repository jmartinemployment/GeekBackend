using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.Rag;

public sealed class RagGenerateServiceTests
{
    [Theory]
    [InlineData("Technical Article", true, RagRetrievalFamily.LongForm)]
    [InlineData("case study", true, RagRetrievalFamily.LongForm)]
    [InlineData("Social Ad", true, RagRetrievalFamily.ShortForm)]
    [InlineData("Short Form", true, RagRetrievalFamily.ShortForm)]
    [InlineData("Competitive Battlecard", true, RagRetrievalFamily.Battlecard)]
    [InlineData("Pitch Slides", true, RagRetrievalFamily.Slides)]
    [InlineData("Strategy Theme", true, RagRetrievalFamily.Slides)]
    [InlineData("nope", false, RagRetrievalFamily.LongForm)]
    public void TryNormalize_and_FamilyOf(string raw, bool ok, RagRetrievalFamily family)
    {
        var parsed = RagWritingIntents.TryNormalize(raw, out var intent);
        Assert.Equal(ok, parsed);
        if (ok)
            Assert.Equal(family, RagWritingIntents.FamilyOf(intent));
    }

    [Fact]
    public void BuildNeed_includes_intent_topic_and_role()
    {
        var need = RagGenerateService.BuildNeed(
            RagWritingIntents.TechnicalArticle,
            "CRM integration playbook",
            ["HubSpot", "Salesforce"],
            "partner");

        Assert.Contains("partner tool research", need, StringComparison.Ordinal);
        Assert.Contains("Technical Article", need, StringComparison.Ordinal);
        Assert.Contains("CRM integration playbook", need, StringComparison.Ordinal);
        Assert.Contains("HubSpot", need, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseVariations_reads_json_array()
    {
        var raw = """
            Here you go:
            {"variations":["Punchy one","Punchy two","Punchy three"]}
            """;
        var variations = RagGenerateService.ParseVariations(raw);
        Assert.Equal(3, variations.Count);
        Assert.Equal("Punchy one", variations[0]);
    }

    [Fact]
    public void ParseBattlecard_reads_structured_json()
    {
        var raw = """
            {
              "partnerSummary": "Strong automation.",
              "competitorSummary": "Broader marketplace.",
              "differentiators": ["Native CRM sync"],
              "risks": ["Feature lag on mobile"]
            }
            """;
        var card = RagGenerateService.ParseBattlecard(raw);
        Assert.NotNull(card);
        Assert.Equal("Strong automation.", card!.PartnerSummary);
        Assert.Single(card.Differentiators);
        Assert.Single(card.Risks);
    }

    [Fact]
    public void ModelRouter_longForm_defaults_to_o3()
    {
        // Clear env so defaults apply in this process (tests may run with env set).
        var prev = Environment.GetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL", null);
            Assert.Equal("o3", RagModelRouter.ResolveModel(RagRetrievalFamily.LongForm));
            Assert.Equal("gpt-4o", RagModelRouter.ResolveModel(RagRetrievalFamily.ShortForm));
            Assert.True(RagModelRouter.IsReasoningModel("o3"));
            Assert.True(RagModelRouter.IsReasoningModel("o1-mini"));
            Assert.False(RagModelRouter.IsReasoningModel("gpt-4o"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL", prev);
        }
    }

    [Fact]
    public void ModelRouter_respects_longform_env_override()
    {
        var prev = Environment.GetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL", "o1-mini");
            Assert.Equal("o1-mini", RagModelRouter.ResolveModel(RagRetrievalFamily.LongForm));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_LONGFORM_MODEL", prev);
        }
    }

    [Fact]
    public void BuildThemeSources_includes_entities_and_pages()
    {
        var themes = RagGenerateService.BuildThemeSources(
            [new GccQuoteablePage("https://partner.example/a", "Partner A", [], ["p"])],
            [new GccQuoteablePage("https://rival.example/b", "Rival B", [], ["c"])],
            ["HubSpot"]);

        Assert.Contains(themes, t => t.Entity == "HubSpot");
        Assert.Contains(themes, t => t.Relationship == "partner-theme");
        Assert.Contains(themes, t => t.Relationship == "competitor-contrast");
    }

    [Fact]
    public void OpenAiProvider_detects_reasoning_models()
    {
        Assert.True(OpenAiProvider.IsReasoningModel("o3"));
        Assert.True(OpenAiProvider.IsReasoningModel("o1-preview"));
        Assert.False(OpenAiProvider.IsReasoningModel("gpt-4o"));
    }
}
