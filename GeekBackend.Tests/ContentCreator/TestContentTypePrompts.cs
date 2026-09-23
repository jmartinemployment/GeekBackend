using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The same registry Program.cs wires, so tests exercise the real prompt sets rather than a stub --
/// a stubbed registry would let a set's outline or prompt wiring drift without a test noticing,
/// which is exactly how Tool's context builder drifted from Pillar's.
/// </summary>
internal static class TestContentTypePrompts
{
    public static IContentTypePromptRegistry Registry()
    {
        var prompts = new ContentPromptBuilder();
        return new ContentTypePromptRegistry(
        [
            new PillarPrompts(prompts),
            new BlogPrompts(prompts),
            new ToolPrompts(prompts),
        ]);
    }
}
