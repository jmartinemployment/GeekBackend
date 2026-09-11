using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GridCsvExportTests
{
    [Fact]
    public void Build_exports_topic_first_and_escapes_commas()
    {
        var grid = new GccV2GridDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "owner",
            "FAQ launch",
            "desc",
            "ready",
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"),
            "{}",
            [
                new(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    0,
                    """{"topic":"Topic Alpha"}""",
                    null,
                    "pending",
                    null,
                    DateTimeOffset.Parse("2026-09-11T12:00:00Z")),
                new(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    1,
                    """{"topic":"Topic, Gamma"}""",
                    """{"result":"FAQ draft for: Topic, Gamma"}""",
                    "succeeded",
                    "",
                    DateTimeOffset.Parse("2026-09-11T12:01:00Z")),
            ],
            []);

        var csv = GccV2GridCsvExport.Build(grid);
        Assert.StartsWith("topic,status,rowIndex,result,error,updatedAt", csv);
        Assert.Contains("Topic Alpha,pending,0,", csv);
        Assert.Contains("\"Topic, Gamma\",succeeded,1,", csv);
        Assert.Equal("FAQ-launch.csv", GccV2GridCsvExport.FileName(grid.Name));
    }
}
