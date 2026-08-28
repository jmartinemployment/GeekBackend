using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Publish;

namespace GeekBackend.Tests;

public sealed class GccV2OutlineApprovalTests
{
    [Fact]
    public void SiblingJobsToAdvance_returns_only_awaiting_outline_siblings()
    {
        var createId = Guid.NewGuid();
        var approvedId = Guid.NewGuid();
        var blogId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        var jobs = new List<GccV2JobDto>
        {
            Job(approvedId, createId, "pillar", "awaiting_outline_approval"),
            Job(blogId, createId, "blog", "awaiting_outline_approval"),
            Job(toolId, createId, "tool", "running"),
            Job(Guid.NewGuid(), createId, "email", "awaiting_outline_approval"),
        };

        var siblings = GccV2OutlineApproval.SiblingJobsToAdvance(jobs, approvedId);

        Assert.Equal(2, siblings.Count);
        Assert.Contains(siblings, j => j.Id == blogId);
        Assert.Contains(siblings, j => j.Id == jobs[3].Id);
        Assert.DoesNotContain(siblings, j => j.Id == approvedId);
        Assert.DoesNotContain(siblings, j => j.Id == toolId);
    }

    private static GccV2JobDto Job(Guid id, Guid createId, string contentType, string status) =>
        new(
            id,
            contentType,
            Guid.NewGuid(),
            "owner",
            createId,
            "plan",
            status,
            0,
            null,
            null,
            null,
            null,
            null,
            0,
            DateTimeOffset.UtcNow,
            null,
            null);
}

public sealed class GccV2PublishTypesTests
{
    [Theory]
    [InlineData("pillar", true, false)]
    [InlineData("blog", true, false)]
    [InlineData("tool", true, false)]
    [InlineData("email", false, true)]
    [InlineData("social", false, true)]
    [InlineData("ads", false, true)]
    [InlineData("image-prompt", false, true)]
    public void Publish_triage_flags(string contentType, bool cms, bool exportOnly)
    {
        Assert.Equal(cms, GccV2PublishTypes.IsCmsPublishType(contentType));
        Assert.Equal(exportOnly, GccV2PublishTypes.IsExportOnlyType(contentType));
    }
}

public sealed class GccV2ExportSkipEvaluatorTests
{
    [Fact]
    public void TryGetSkipReason_empty_result()
    {
        Assert.Equal("No completed result yet.", GccV2ExportSkipEvaluator.TryGetSkipReason(null));
        Assert.Equal("No completed result yet.", GccV2ExportSkipEvaluator.TryGetSkipReason(""));
    }

    [Fact]
    public void TryGetSkipReason_invalid_json()
    {
        Assert.Equal("Result could not be parsed.", GccV2ExportSkipEvaluator.TryGetSkipReason("{not-json"));
    }

    [Fact]
    public void TryGetSkipReason_missing_document()
    {
        const string json = """{"title":"Hello","document":null}""";
        Assert.Equal("No document in result.", GccV2ExportSkipEvaluator.TryGetSkipReason(json));
    }

    [Fact]
    public void TryGetSkipReason_exportable_returns_null()
    {
        const string json = """
            {
              "title": "Hello",
              "document": {
                "lede": { "tag": "h2", "heading": "Hi", "paragraphs": [], "children": [] },
                "sections": []
              }
            }
            """;
        Assert.Null(GccV2ExportSkipEvaluator.TryGetSkipReason(json));
    }
}
