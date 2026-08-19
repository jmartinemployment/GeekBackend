using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Infrastructure.Serialization;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests;

public class ProjectSnapshotSerializerTests
{
    [Fact]
    public void Roundtrip_keeps_paragraph_runs()
    {
        var words = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"word{i}"));
        var body = new ContentDocument(
            new Section("h2", "Lede", [new TextParagraph([new Run(words)])], null, []),
            [new Section("h2", "Overview", [new TextParagraph([new Run(words)])], null, [])]);
        var liveWords = ContentDocumentText.CountWords(body);
        Assert.True(liveWords >= 20);

        var project = new Project
        {
            Name = "persist-test",
            TargetKeyword = "test",
            GeneratedContents =
            [
                new GeneratedContent
                {
                    ContentType = GeneratedContentType.ToolPost,
                    Title = "ChatGPT",
                    Body = body,
                    WordCount = liveWords,
                },
            ],
        };

        var json = ProjectSnapshotSerializer.Serialize(project);
        Assert.Contains("\"runs\"", json, StringComparison.OrdinalIgnoreCase);

        var roundtrip = ProjectSnapshotSerializer.Deserialize(json, null);
        var saved = Assert.Single(roundtrip.GeneratedContents);
        Assert.Equal(liveWords, ContentDocumentText.CountWords(saved.Body));
    }
}
