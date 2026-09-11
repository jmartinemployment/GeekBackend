namespace GeekBackend.Tests.ContentCreatorV2;

using GeekAPI.Services.ContentCreatorV2;

public sealed class GccV2GridTopicImportTests
{
    [Fact]
    public void Parse_splits_newlines_csv_first_column_and_dedupes()
    {
        var topics = GccV2GridTopicImport.Parse(
            "Topic Alpha\nTopic Beta,extra\n\"Topic, Gamma\"\nTopic Alpha\n\n");

        Assert.Equal(
            ["Topic Alpha", "Topic Beta", "Topic, Gamma"],
            topics);
    }

    [Fact]
    public void Parse_merges_explicit_topics_and_rejects_blank_input()
    {
        Assert.Empty(GccV2GridTopicImport.Parse("  \n , \n"));
        var topics = GccV2GridTopicImport.Parse(
            "From paste\nBrand new",
            ["Explicit One", "from paste", "Explicit Two"]);
        Assert.Equal(
            ["Explicit One", "from paste", "Explicit Two", "Brand new"],
            topics);
    }
}
