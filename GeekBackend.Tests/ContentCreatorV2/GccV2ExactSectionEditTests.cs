using System.Text.Json;
using GeekAPI.Controllers.ContentCreatorV2;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ExactSectionEditTests
{
    [Fact]
    public void Exact_replacement_changes_only_root_body()
    {
        var child = new Section(
            "h3",
            "Evidence details",
            [new TextParagraph([new Run("Keep nested evidence.")])],
            "https://example.test/evidence",
            [],
            "keep image");
        var current = new Section(
            "h2",
            "Stable heading",
            [new TextParagraph([new Run("Old body")])],
            "https://example.test/root",
            [child],
            "keep root image");

        var replaced = GccV2CanvasController.BuildExactBodyReplacement(
            current,
            "First exact paragraph.\r\n\r\nSecond exact paragraph.");

        Assert.Equal(current.Tag, replaced.Tag);
        Assert.Equal(current.Heading, replaced.Heading);
        Assert.Equal(current.Href, replaced.Href);
        Assert.Equal(current.Children, replaced.Children);
        Assert.Equal(current.ImagePrompt, replaced.ImagePrompt);
        Assert.Equal(2, replaced.Paragraphs.Count);
        Assert.Equal(
            ["First exact paragraph.", "Second exact paragraph."],
            replaced.Paragraphs
                .Cast<TextParagraph>()
                .Select(paragraph => paragraph.Runs.Single().Text)
                .ToArray());
    }

    [Fact]
    public void Result_merge_preserves_artifact_and_marks_validation_invalid()
    {
        var document = new ContentDocument(
            new Section("h2", "Lede", [new TextParagraph([new Run("Edited")])], null, []),
            []);
        var at = DateTimeOffset.Parse("2026-09-08T18:00:00Z");

        var merged = GccV2CanvasController.MergeDocumentIntoResultJson(
            """{"title":"Draft","linkedInCarousel":{"slug":"durable","pdfBase64":"JVBERg=="}}""",
            document,
            at);

        using var json = JsonDocument.Parse(merged);
        Assert.Equal("Draft", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "JVBERg==",
            json.RootElement.GetProperty("linkedInCarousel").GetProperty("pdfBase64").GetString());
        Assert.Equal(
            "Edited",
            json.RootElement.GetProperty("document")
                .GetProperty("lede")
                .GetProperty("paragraphs")[0]
                .GetProperty("runs")[0]
                .GetProperty("text")
                .GetString());
        Assert.False(json.RootElement.GetProperty("validationState").GetProperty("valid").GetBoolean());
        Assert.Equal(
            "operator-section-edit",
            json.RootElement.GetProperty("validationState").GetProperty("reason").GetString());
    }
}
