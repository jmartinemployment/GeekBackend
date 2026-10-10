using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Providers;
using GeekBackend.Tests.ContentCreator;

namespace GeekBackend.Tests.Rag;

/// <summary>
/// RAG is retrieval and verification only. The library reports what it can do and indexes ad
/// templates; it never generates, and its status says so whatever else is switched on.
/// </summary>
public sealed class RagLibraryTests
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
    public void The_status_never_offers_generation()
    {
        var status = new RagLibrary(new GccCompetitorAnalysisResolverTests.FakeRag()).GetStatus();

        Assert.True(status.Available);
        Assert.True(status.RagClientEnabled);
        Assert.Null(status.Reason);
        Assert.False(status.GenerateEnabled);
        Assert.False(status.CiteableGenerateAvailable);
        Assert.Equal(RagWritingIntents.All, status.WritingIntents);
        Assert.Equal(RagEntitySeedList.Names, status.EntitySeeds);
    }

    [Fact]
    public async Task Indexing_ad_templates_refuses_when_the_index_is_switched_off()
    {
        var previous = Environment.GetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED", "0");
            var library = new RagLibrary(new GccCompetitorAnalysisResolverTests.FakeRag());

            Assert.False(library.GetStatus().AdTemplateIndexAvailable);
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                library.IndexAdTemplatesAsync(
                    [new RagAdTemplateDto { Id = "t1", Name = "Hook", Body = "Body." }], CancellationToken.None));
            Assert.Contains("GEEK_RAG_AD_TEMPLATES_ENABLED", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED", previous);
        }
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
    public void OpenAiProvider_detects_reasoning_models()
    {
        Assert.True(OpenAiProvider.IsReasoningModel("o3"));
        Assert.True(OpenAiProvider.IsReasoningModel("o1-preview"));
        Assert.False(OpenAiProvider.IsReasoningModel("gpt-4o"));
    }
}
