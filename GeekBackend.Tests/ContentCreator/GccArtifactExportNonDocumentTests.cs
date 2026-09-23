using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The export button returned 500 (Jeff, 2026-09-23). Not every artifact on a create is a
/// ContentDocument -- an image-prompt pack and a metadata artifact are flat objects -- and
/// System.Text.Json does not enforce a record's non-nullable parameters, so those deserialize
/// into a ContentDocument with a null Lede rather than failing. That passed the null check on the
/// document and threw inside the renderer, taking the whole archive down with it.
/// </summary>
public class GccArtifactExportNonDocumentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AFlatArtifactDeserializesIntoADocumentWithNoLede()
    {
        // The mechanism, stated outright: this does not throw and does not return null.
        const string imagePromptArtifact = """{"prompt":"A desk at month-end.","negativePrompt":"text"}""";

        var parsed = JsonSerializer.Deserialize<ContentDocument>(imagePromptArtifact, Json);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Lede);
    }

    [Fact]
    public void ADocumentWithNoLedeStillRendersRatherThanThrowing()
    {
        // Belt as well as braces: the export skips these now, but a renderer that throws on a
        // missing lede turns one odd artifact into a failed export for every other one beside it.
        var document = new ContentDocument(
            null!,
            [new Section("h2", "What the rollout takes", [new TextParagraph([new Run("Body.")])], null, [])]);

        var html = GeekAPI.Services.Workflow.Services.Export.SectionHtmlRenderer.RenderDocument(
            title: "Automated Accounts Payable",
            description: null,
            canonicalUrl: null,
            ogType: "article",
            ogImage: null,
            jsonLdSchema: null,
            additionalMeta: new Dictionary<string, string?>(),
            body: document);

        Assert.Contains("What the rollout takes", html, StringComparison.Ordinal);
    }
}
