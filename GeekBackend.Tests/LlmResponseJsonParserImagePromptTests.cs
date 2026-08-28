using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests;

public sealed class LlmResponseJsonParserImagePromptTests
{
    [Fact]
    public void ParseSectionImagePrompts_accepts_extra_sections_when_target_matches()
    {
        const string json = """
            {
              "sections": [
                {
                  "sourceType": "pillar-hero",
                  "heading": "Enterprise AI Guide",
                  "order": 0,
                  "prompt": "Hero establishing shot of enterprise AI transformation.",
                  "width": 1024,
                  "height": 576,
                  "imageModel": "leonardo-diffusion-xl",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false,
                  "notes": "No text in image"
                },
                {
                  "sourceType": "pillar",
                  "heading": "Implementation Framework",
                  "order": 1,
                  "prompt": "Diagram of phased AI rollout.",
                  "width": 1024,
                  "height": 576,
                  "imageModel": "leonardo-diffusion-xl",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                }
              ]
            }
            """;

        var expected = new ImagePromptSectionTarget("pillar-hero", "Enterprise AI Guide", 0);
        var parsed = LlmResponseJsonParser.ParseSectionImagePrompts(json, [expected], "image prompt");

        Assert.Single(parsed.Sections);
        Assert.Equal("pillar-hero", parsed.Sections[0].SourceType);
        Assert.Contains("Hero establishing shot", parsed.Sections[0].Prompt);
    }
}
